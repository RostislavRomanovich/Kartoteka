using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Xml.Linq;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Policy = "CanExport")]
[Route("api/export")]
public sealed class ExportController(AppDbContext db) : ControllerBase
{
    private sealed record Field(string Key, string Label, bool Sensitive = false);
    private static readonly Field[] Fields =
    [
        new("personnelNumber", "Табельный номер"), new("lastName", "Фамилия"), new("firstName", "Имя"),
        new("middleName", "Отчество"), new("dateOfBirth", "Дата рождения", true),
        new("position", "Должность"), new("subdivision", "Подразделение (текст)"), new("organizationalUnitName", "Подразделение"),
        new("rank", "Звание"), new("maritalStatus", "Семейное положение", true),
        new("passportSeries", "Серия паспорта", true), new("passportNumber", "Номер паспорта", true),
        new("passportIssuedBy", "Кем выдан паспорт", true), new("passportIssuedOn", "Дата выдачи паспорта", true),
        new("militaryIdNumber", "Номер военного билета", true), new("militaryRegistrationOffice", "Военкомат", true),
        new("militaryRegistrationCategory", "Категория воинского учёта", true), new("militaryRegistrationStatus", "Статус воинского учёта", true),
        new("transferDate", "Дата перевода"), new("transferFrom", "Откуда переведён"),
        new("accessForm", "Форма допуска", true), new("accessGrantedOn", "Дата оформления допуска", true), new("accessIssuedBy", "Кем оформлен допуск", true),
        new("contractStart", "Начало контракта"), new("contractTermMonths", "Срок контракта, месяцев"), new("contractEnd", "Окончание контракта"),
        new("carsCount", "Количество автомобилей"), new("relativesCount", "Количество родственников"),
        new("awardsCount", "Количество наград"), new("tripsCount", "Количество командировок"),
        new("carsSummary", "Автомобили (сводно)"), new("licenseCategoriesSummary", "Категории водительских прав"),
        new("relativesSummary", "Родственники (сводно)", true), new("positionHistorySummary", "История должностей"),
        new("classnessSummary", "Классность"), new("awardsSummary", "Награды (сводно)"),
        new("tripsSummary", "Командировки (сводно)"), new("messengersSummary", "Контакты и мессенджеры", true),
        new("personalFeaturesSummary", "Особые приметы", true),
        new("isArchived", "В архиве"), new("createdUtc", "Создано"), new("updatedUtc", "Обновлено")
    ];
    private static readonly HashSet<string> SensitiveKeys = Fields.Where(x => x.Sensitive).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);

    [HttpGet("fields")]
    public async Task<IActionResult> GetFields(CancellationToken cancellationToken)
    {
        var adminOrEditor = User.IsInRole("admin") || User.IsInRole("editor");
        var result = Fields.Where(f => adminOrEditor || !f.Sensitive).Select(f => new { f.Key, f.Label }).ToList();
        if (adminOrEditor)
        {
            var customFields = await db.CustomFieldDefinitions.AsNoTracking().Where(x => x.Scope == "employee" && x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Label).Select(x => new { Key = "customField_" + x.Id, x.Label }).ToListAsync(cancellationToken);
            result.AddRange(customFields);
        }
        return Ok(result);
    }

    [HttpPost("table")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> ExportTable([FromBody] ExportRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields is null || request.Fields.Count == 0)
            return BadRequest(new { error = "Выберите хотя бы один столбец." });
        var requestedFields = request.Fields.Distinct(StringComparer.Ordinal).ToList();
        var fieldMap = Fields.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var customFieldIds = requestedFields.Where(x => x.StartsWith("customField_", StringComparison.Ordinal))
            .Select(x => long.TryParse(x[12..], out var id) ? id : 0).Where(id => id > 0).Distinct().ToArray();
        if (!(User.IsInRole("admin") || User.IsInRole("editor")) && customFieldIds.Length > 0) return Forbid();
        var customDefinitions = await db.CustomFieldDefinitions.AsNoTracking().Where(x => customFieldIds.Contains(x.Id) && x.Scope == "employee" && x.IsActive)
            .Select(x => new { x.Id, x.Label }).ToListAsync(cancellationToken);
        foreach (var custom in customDefinitions) fieldMap["customField_" + custom.Id] = new Field("customField_" + custom.Id, custom.Label);
        if (requestedFields.Any(x => !fieldMap.ContainsKey(x)))
            return BadRequest(new { error = "Запрошен неизвестный столбец." });
        if (!(User.IsInRole("admin") || User.IsInRole("editor")) && requestedFields.Any(SensitiveKeys.Contains))
            return Forbid();
        if (request.EmployeeIds is { Count: > 10000 })
            return BadRequest(new { error = "За один экспорт можно выбрать не более 10 000 сотрудников." });

        var query = db.Employees.AsNoTracking().Where(e => !e.IsArchived);
        if (request.EmployeeIds is { Count: > 0 })
            query = query.Where(e => request.EmployeeIds.Contains(e.Id));
        else if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim();
            query = query.Where(e => EF.Functions.ILike(e.LastName, $"%{term}%") ||
                EF.Functions.ILike(e.FirstName, $"%{term}%") ||
                (e.MiddleName != null && EF.Functions.ILike(e.MiddleName, $"%{term}%")) ||
                EF.Functions.ILike(e.PersonnelNumber, $"%{term}%"));
        }
        var employees = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        if (request.EmployeeIds is { Count: > 0 } && employees.Count != request.EmployeeIds.Distinct().Count())
            return BadRequest(new { error = "Один или несколько выбранных сотрудников не найдены либо находятся в архиве." });
        if (employees.Count > 10000) return BadRequest(new { error = "Слишком много строк для одного экспорта. Уточните фильтр." });

        var ids = employees.Select(e => e.Id).ToArray();
        var cars = await db.EmployeeCars.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).GroupBy(x => x.EmployeeId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var relatives = await db.Relatives.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).GroupBy(x => x.EmployeeId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var awards = await db.Awards.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).GroupBy(x => x.EmployeeId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var trips = await db.BusinessTrips.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).GroupBy(x => x.EmployeeId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);
        var unitIds = employees.Where(e => e.OrganizationalUnitId.HasValue).Select(e => e.OrganizationalUnitId!.Value).Distinct().ToArray();
        var unitNames = await db.OrganizationalUnits.AsNoTracking().Where(x => unitIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var customIdsToRead = customDefinitions.Select(x => x.Id).ToArray();
        var customValues = await db.EmployeeCustomFieldValues.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && customIdsToRead.Contains(x.FieldDefinitionId))
            .ToDictionaryAsync(x => (x.EmployeeId, x.FieldDefinitionId), x => x.Value, cancellationToken);
        var relatedSummaries = new Dictionary<string, Dictionary<long, string>>(StringComparer.Ordinal);
        void AddSummary<T>(string key, IEnumerable<T> records, Func<T, long> employeeId, Func<T, string> value)
            => relatedSummaries[key] = records.GroupBy(employeeId).ToDictionary(g => g.Key, g => string.Join("; ", g.Select(value).Where(v => !string.IsNullOrWhiteSpace(v))));
        var carRows = await db.EmployeeCars.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.MakeModel, x.RegistrationPlate, x.ProductionYear }).ToListAsync(cancellationToken);
        AddSummary("carsSummary", carRows, x => x.EmployeeId, x => string.Join(" ", new[] { x.MakeModel, x.RegistrationPlate, x.ProductionYear?.ToString() }.Where(v => !string.IsNullOrWhiteSpace(v))));
        var licenseRows = await db.DrivingLicenseCategories.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.Category, x.ExpiresOn }).ToListAsync(cancellationToken);
        AddSummary("licenseCategoriesSummary", licenseRows, x => x.EmployeeId, x => x.Category + (x.ExpiresOn.HasValue ? " до " + x.ExpiresOn.Value.ToString("dd.MM.yyyy") : ""));
        var relativeRows = await db.Relatives.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.FullName, x.Relationship, x.DateOfBirth }).ToListAsync(cancellationToken);
        AddSummary("relativesSummary", relativeRows, x => x.EmployeeId, x => x.FullName + (string.IsNullOrWhiteSpace(x.Relationship) ? "" : " (" + x.Relationship + ")") + (x.DateOfBirth.HasValue ? ", " + x.DateOfBirth.Value.ToString("dd.MM.yyyy") : ""));
        var positionRows = await db.PositionHistories.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.Position, x.StartDate, x.EndDate }).ToListAsync(cancellationToken);
        AddSummary("positionHistorySummary", positionRows, x => x.EmployeeId, x => x.Position + " (" + x.StartDate.ToString("dd.MM.yyyy") + " — " + (x.EndDate.HasValue ? x.EndDate.Value.ToString("dd.MM.yyyy") : "по настоящее время") + ")");
        var classnessRows = await db.ClassnessHistories.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.Classness, x.GrantedOn, x.ReviewDueOn }).ToListAsync(cancellationToken);
        AddSummary("classnessSummary", classnessRows, x => x.EmployeeId, x => x.Classness + " с " + x.GrantedOn.ToString("dd.MM.yyyy") + (x.ReviewDueOn.HasValue ? ", пересмотр до " + x.ReviewDueOn.Value.ToString("dd.MM.yyyy") : ""));
        var awardRows = await db.Awards.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.Name, x.AwardedOn }).ToListAsync(cancellationToken);
        AddSummary("awardsSummary", awardRows, x => x.EmployeeId, x => x.Name + (x.AwardedOn.HasValue ? " (" + x.AwardedOn.Value.ToString("dd.MM.yyyy") + ")" : ""));
        var tripRows = await db.BusinessTrips.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.Destination, x.StartDate, x.EndDate }).ToListAsync(cancellationToken);
        AddSummary("tripsSummary", tripRows, x => x.EmployeeId, x => x.Destination + " (" + x.StartDate.ToString("dd.MM.yyyy") + " — " + x.EndDate.ToString("dd.MM.yyyy") + ")");
        var messengerRows = await db.EmployeeMessengers.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.Messenger, x.Contact }).ToListAsync(cancellationToken);
        AddSummary("messengersSummary", messengerRows, x => x.EmployeeId, x => x.Messenger + ": " + x.Contact);
        var featureRows = await db.PersonalFeatures.AsNoTracking().Where(x => ids.Contains(x.EmployeeId)).Select(x => new { x.EmployeeId, x.FeatureType, x.Description, x.LocationOnBody }).ToListAsync(cancellationToken);
        AddSummary("personalFeaturesSummary", featureRows, x => x.EmployeeId, x => x.FeatureType + ": " + x.Description + (string.IsNullOrWhiteSpace(x.LocationOnBody) ? "" : " (" + x.LocationOnBody + ")"));
        var rows = employees.Select(e => requestedFields.Select(key =>
        {
            if (key.StartsWith("customField_", StringComparison.Ordinal) && long.TryParse(key[12..], out var customId))
                return customValues.GetValueOrDefault((e.Id, customId)) ?? "";
            if (relatedSummaries.TryGetValue(key, out var byEmployee) && byEmployee.TryGetValue(e.Id, out var summary)) return summary;
            return ValueFor(key, e, unitNames, cars, relatives, awards, trips);
        }).ToArray()).ToList();
        var headers = requestedFields.Select(key => fieldMap[key].Label).ToArray();
        var format = (request.Format ?? "docx").Trim().ToLowerInvariant();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return format switch
        {
            "csv" => File(ToCsv(headers, rows), "text/csv; charset=utf-8", $"employees_{stamp}.csv"),
            "docx" => File(ToDocx(headers, rows, stamp), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"employees_{stamp}.docx"),
            "xlsx" => File(ToXlsx(headers, rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"employees_{stamp}.xlsx"),
            "print" => Content(ToPrintableHtml(headers, rows), "text/html; charset=utf-8"),
            "preview" => Ok(new { headers, rows, count = rows.Count }),
            _ => BadRequest(new { error = "Поддерживаются Word (docx), Excel (xlsx), CSV и печать." })
        };
    }

    // Legacy endpoint retained for existing clients and scripts.
    [HttpGet("employees.csv")]
    public async Task<IActionResult> EmployeesCsv()
    {
        var employees = await db.Employees.AsNoTracking().Where(e => !e.IsArchived)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(e => new { e.PersonnelNumber, e.LastName, e.FirstName, e.MiddleName, e.Position, e.Subdivision, e.Rank, e.ContractStart, e.ContractEnd }).ToListAsync();
        var headers = new[] { "Табельный номер", "Фамилия", "Имя", "Отчество", "Должность", "Подразделение", "Звание", "Начало контракта", "Окончание контракта" };
        var rows = employees.Select(e => new[] { e.PersonnelNumber, e.LastName, e.FirstName, e.MiddleName, e.Position, e.Subdivision, e.Rank, e.ContractStart?.ToString("dd.MM.yyyy"), e.ContractEnd?.ToString("dd.MM.yyyy") }.Select(x => x ?? "").ToArray()).ToList();
        return File(ToCsv(headers, rows), "text/csv; charset=utf-8", $"employees_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    private static string ValueFor(string key, Employee e, Dictionary<long, string> units, Dictionary<long, int> cars, Dictionary<long, int> relatives, Dictionary<long, int> awards, Dictionary<long, int> trips)
    {
        object? value = key switch
        {
            "personnelNumber" => e.PersonnelNumber, "lastName" => e.LastName, "firstName" => e.FirstName, "middleName" => e.MiddleName,
            "dateOfBirth" => e.DateOfBirth, "position" => e.Position, "subdivision" => e.Subdivision,
            "organizationalUnitName" => e.OrganizationalUnitId is long uid && units.TryGetValue(uid, out var unit) ? unit : null,
            "rank" => e.Rank, "maritalStatus" => e.MaritalStatus, "passportSeries" => e.PassportSeries, "passportNumber" => e.PassportNumber,
            "passportIssuedBy" => e.PassportIssuedBy, "passportIssuedOn" => e.PassportIssuedOn, "militaryIdNumber" => e.MilitaryIdNumber,
            "militaryRegistrationOffice" => e.MilitaryRegistrationOffice, "militaryRegistrationCategory" => e.MilitaryRegistrationCategory,
            "militaryRegistrationStatus" => e.MilitaryRegistrationStatus, "transferDate" => e.TransferDate, "transferFrom" => e.TransferFrom,
            "accessForm" => e.AccessForm, "accessGrantedOn" => e.AccessGrantedOn, "accessIssuedBy" => e.AccessIssuedBy,
            "contractStart" => e.ContractStart, "contractTermMonths" => e.ContractTermMonths, "contractEnd" => e.ContractEnd,
            "carsCount" => cars.GetValueOrDefault(e.Id), "relativesCount" => relatives.GetValueOrDefault(e.Id),
            "awardsCount" => awards.GetValueOrDefault(e.Id), "tripsCount" => trips.GetValueOrDefault(e.Id),
            "isArchived" => e.IsArchived ? "Да" : "Нет", "createdUtc" => e.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            "updatedUtc" => e.UpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), _ => null
        };
        return value switch { null => "", DateOnly date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), DateTime dateTime => dateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? "" };
    }

    private static byte[] ToCsv(string[] headers, List<string[]> rows)
    {
        static string Csv(string? value)
        {
            value ??= "";
            if (value.TrimStart().Length > 0 && "=+-@".Contains(value.TrimStart()[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder(); sb.AppendLine(string.Join(";", headers.Select(Csv)));
        foreach (var row in rows) sb.AppendLine(string.Join(";", row.Select(Csv)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static byte[] ToDocx(string[] headers, List<string[]> rows, string stamp)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var body = new XElement(w + "body");
        body.Add(new XElement(w + "p", new XElement(w + "pPr", new XElement(w + "jc", new XAttribute(w + "val", "center"))), new XElement(w + "r", new XElement(w + "rPr", new XElement(w + "b")), new XElement(w + "t", "Таблица сотрудников"))));
        body.Add(new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", $"Сформировано: {DateTime.Now:dd.MM.yyyy HH:mm}; сотрудников: {rows.Count}"))));
        var table = new XElement(w + "tbl", new XElement(w + "tblPr", new XElement(w + "tblBorders",
            new XElement(w + "top", new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4")), new XElement(w + "left", new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4")),
            new XElement(w + "bottom", new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4")), new XElement(w + "right", new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4")),
            new XElement(w + "insideH", new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4")), new XElement(w + "insideV", new XAttribute(w + "val", "single"), new XAttribute(w + "sz", "4")))));
        XElement Cell(string text, bool header) => new(w + "tc", new XElement(w + "tcPr", header ? new XElement(w + "shd", new XAttribute(w + "fill", "D9E2F3")) : null), new XElement(w + "p", new XElement(w + "pPr", new XElement(w + "spacing", new XAttribute(w + "after", "0"))), new XElement(w + "r", header ? new XElement(w + "rPr", new XElement(w + "b")) : null, new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? ""))));
        table.Add(new XElement(w + "tr", new XElement(w + "trPr", new XElement(w + "tblHeader", new XAttribute(w + "val", "true"))), headers.Select(h => Cell(h, true))));
        foreach (var row in rows) table.Add(new XElement(w + "tr", row.Select(v => Cell(v, false))));
        body.Add(table); body.Add(new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", "16838"), new XAttribute(w + "h", "11906"), new XAttribute(w + "orient", "landscape")), new XElement(w + "pgMar", new XAttribute(w + "top", "720"), new XAttribute(w + "right", "540"), new XAttribute(w + "bottom", "720"), new XAttribute(w + "left", "540"), new XAttribute(w + "header", "360"), new XAttribute(w + "footer", "360"))));
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", w), body));
        var types = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement("Types", new XAttribute("xmlns", "http://schemas.openxmlformats.org/package/2006/content-types"), new XElement("Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement("Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), new XElement("Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))));
        var rels = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement("Relationships", new XAttribute("xmlns", "http://schemas.openxmlformats.org/package/2006/relationships"), new XElement("Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml"))));
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "[Content_Types].xml", types); WriteEntry(zip, "_rels/.rels", rels); WriteEntry(zip, "word/document.xml", document);
        }
        return memory.ToArray();
    }
    private static string ToPrintableHtml(string[] headers, List<string[]> rows)
    {
        Func<string, string> encode = HtmlEncoder.Default.Encode;
        var sb = new StringBuilder("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>Таблица сотрудников</title><style>body{font:12px Arial,sans-serif;color:#111}h1{font-size:18px}p{color:#555}table{border-collapse:collapse;width:100%}th,td{border:1px solid #777;padding:5px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:#e5eaf2}thead{display:table-header-group}@media print{button{display:none}body{margin:8mm}table{font-size:9px}}</style></head><body><button onclick=\"window.print()\">Печать</button><h1>Таблица сотрудников</h1><p>Сформировано: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + "; сотрудников: " + rows.Count + "</p><table><thead><tr>");
        foreach (var header in headers) sb.Append("<th>").Append(encode(header)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows) { sb.Append("<tr>"); foreach (var value in row) sb.Append("<td>").Append(encode(value ?? "").Replace("\n", "<br>", StringComparison.Ordinal)).Append("</td>"); sb.Append("</tr>"); }
        return sb.Append("</tbody></table></body></html>").ToString();
    }

    private static byte[] ToXlsx(string[] headers, List<string[]> rows)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentType = "http://schemas.openxmlformats.org/package/2006/content-types";
        string ColumnName(int index)
        {
            var name = ""; while (index > 0) { index--; name = (char)('A' + index % 26) + name; index /= 26; } return name;
        }
        XElement Cell(string address, string value, bool header = false) => new(main + "c", new XAttribute("r", address), new XAttribute("t", "inlineStr"), new XElement(main + "is", new XElement(main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? "")));
        var sheetData = new XElement(main + "sheetData");
        var firstRow = new XElement(main + "row", new XAttribute("r", 1));
        for (var i = 0; i < headers.Length; i++) firstRow.Add(Cell(ColumnName(i + 1) + "1", headers[i], true));
        sheetData.Add(firstRow);
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(main + "row", new XAttribute("r", r + 2));
            for (var c = 0; c < rows[r].Length; c++) row.Add(Cell(ColumnName(c + 1) + (r + 2), rows[r][c]));
            sheetData.Add(row);
        }
        var worksheet = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(main + "worksheet", new XAttribute(XNamespace.Xmlns + "r", rel), sheetData));
        var workbook = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(main + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(main + "sheets", new XElement(main + "sheet", new XAttribute("name", "Сотрудники"), new XAttribute("sheetId", "1"), new XAttribute(rel + "id", "rId1")))));
        var contentTypes = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(contentType + "Types", new XElement(contentType + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(contentType + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")), new XElement(contentType + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")), new XElement(contentType + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
        var rootRels = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(packageRel + "Relationships", new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
        var workbookRels = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(packageRel + "Relationships", new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml"))));
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes); WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "xl/workbook.xml", workbook); WriteEntry(zip, "xl/_rels/workbook.xml.rels", workbookRels); WriteEntry(zip, "xl/worksheets/sheet1.xml", worksheet);
        }
        return memory.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, XDocument doc)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal); using var stream = entry.Open(); using var writer = new StreamWriter(stream, new UTF8Encoding(false)); doc.Save(writer);
    }
    public sealed class ExportRequest { public List<long>? EmployeeIds { get; set; } public string? Query { get; set; } public List<string>? Fields { get; set; } public string? Format { get; set; } }
}
