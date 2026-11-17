using System.Security.Claims;
using Kartoteka.Server.Contracts;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/employees")]
public sealed class EmployeesController(AppDbContext db, IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    // GET /api/employees?q=ivanov&page=1&pageSize=50&includeArchived=false
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeDto>>> List(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool includeArchived = false)
    {
        page = Math.Clamp(page, 1, 100000);
        pageSize = Math.Clamp(pageSize, 1, 200);
        if (includeArchived && !User.IsInRole("admin"))
            return Forbid();

        var query = db.Employees.AsNoTracking().Include(e => e.OrganizationalUnit).AsQueryable();
        if (!includeArchived) query = query.Where(e => !e.IsArchived);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var normalizedNumber = NormalizePersonnelNumber(term);
            query = query.Where(e => e.LastName.Contains(term) || e.FirstName.Contains(term) ||
                (e.MiddleName != null && e.MiddleName.Contains(term)) ||
                e.PersonnelNumber.Contains(normalizedNumber));
        }

        var rows = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ThenBy(e => e.MiddleName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<EmployeeDto>> Get(long id)
    {
        var e = await db.Employees.AsNoTracking().Include(x => x.OrganizationalUnit)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null || (e.IsArchived && !User.IsInRole("admin"))) return NotFound();
        return Ok(ToDto(e));
    }

    [HttpPost]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<ActionResult<EmployeeDto>> Create(CreateEmployeeRequest request)
    {
        var validation = await ValidateRequestAsync(request);
        if (validation is not null) return BadRequest(new { error = validation });

        var normalizedNumber = NormalizePersonnelNumber(request.PersonnelNumber);
        if (normalizedNumber.Length > 0 && await db.Employees.AnyAsync(x => x.PersonnelNumber == normalizedNumber))
            return Conflict(new { error = "Сотрудник с таким табельным номером уже существует." });

        var employee = new Employee();
        Apply(employee, request);
        employee.CreatedUtc = DateTime.UtcNow;
        employee.UpdatedUtc = DateTime.UtcNow;
        db.Employees.Add(employee);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "Запись с таким уникальным значением уже существует." }); }

        await AuditAsync("employee_created", "employee", employee.Id.ToString());
        await db.Entry(employee).Reference(x => x.OrganizationalUnit).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToDto(employee));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<ActionResult<EmployeeDto>> Update(long id, CreateEmployeeRequest request)
    {
        var validation = await ValidateRequestAsync(request);
        if (validation is not null) return BadRequest(new { error = validation });

        var employee = await db.Employees.Include(x => x.OrganizationalUnit).FirstOrDefaultAsync(x => x.Id == id);
        if (employee is null || employee.IsArchived) return NotFound();
        var normalizedNumber = NormalizePersonnelNumber(request.PersonnelNumber);
        if (normalizedNumber.Length > 0 && await db.Employees.AnyAsync(x => x.Id != id && x.PersonnelNumber == normalizedNumber))
            return Conflict(new { error = "Сотрудник с таким табельным номером уже существует." });

        Apply(employee, request);
        employee.UpdatedUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "Запись с таким уникальным значением уже существует." }); }

        await AuditAsync("employee_updated", "employee", employee.Id.ToString());
        db.Entry(employee).Reference(x => x.OrganizationalUnit).IsLoaded = false;
        await db.Entry(employee).Reference(x => x.OrganizationalUnit).LoadAsync();
        return Ok(ToDto(employee));
    }

    // Deletion is archival by default to preserve history and avoid accidental cascade loss.
    [HttpDelete("{id:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> Archive(long id)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        if (employee.IsArchived) return NoContent();
        employee.IsArchived = true;
        employee.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await AuditAsync("employee_archived", "employee", id.ToString());
        return NoContent();
    }

    [HttpPost("{id:long}/restore")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> Restore(long id)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        if (!employee.IsArchived) return NoContent();
        employee.IsArchived = false;
        employee.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await AuditAsync("employee_restored", "employee", id.ToString());
        return NoContent();
    }

    private async Task<string?> ValidateRequestAsync(CreateEmployeeRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.LastName) || string.IsNullOrWhiteSpace(r.FirstName))
            return "Фамилия и имя обязательны.";
        if (r.ContractTermMonths is not null && r.ContractStart is null)
            return "Для срока контракта укажите дату начала.";
        if (r.ContractTermMonths is <= 0 or > 600)
            return "Срок контракта должен быть от 1 до 600 месяцев.";
        if (r.ContractStart is not null && r.ContractTermMonths is not null && r.ContractEnd is not null && r.ContractEnd < r.ContractStart)
            return "Дата окончания контракта не может быть раньше даты начала.";
        if (r.DateOfBirth is not null && r.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            return "Дата рождения не может быть в будущем.";
        if (r.OrganizationalUnitId is not null && !await db.OrganizationalUnits.AnyAsync(u => u.Id == r.OrganizationalUnitId && u.IsActive))
            return "Указанное подразделение не найдено или неактивно.";
        return null;
    }

    private static void Apply(Employee e, CreateEmployeeRequest r)
    {
        e.PersonnelNumber = NormalizePersonnelNumber(r.PersonnelNumber);
        e.LastName = NormalizeName(r.LastName);
        e.FirstName = NormalizeName(r.FirstName);
        e.MiddleName = string.IsNullOrWhiteSpace(r.MiddleName) ? null : NormalizeName(r.MiddleName);
        e.DateOfBirth = r.DateOfBirth;
        e.PassportSeries = Clean(r.PassportSeries);
        e.PassportNumber = Clean(r.PassportNumber);
        e.PassportIssuedBy = Clean(r.PassportIssuedBy);
        e.PassportIssuedOn = r.PassportIssuedOn;
        e.Position = Clean(r.Position);
        e.Subdivision = Clean(r.Subdivision);
        e.Rank = Clean(r.Rank);
        e.MilitaryIdNumber = Clean(r.MilitaryIdNumber);
        e.MilitaryRegistrationOffice = Clean(r.MilitaryRegistrationOffice);
        e.MilitaryRegistrationCategory = Clean(r.MilitaryRegistrationCategory);
        e.MilitaryRegistrationStatus = Clean(r.MilitaryRegistrationStatus);
        e.TransferDate = r.TransferDate;
        e.TransferFrom = Clean(r.TransferFrom);
        e.OrganizationalUnitId = r.OrganizationalUnitId;
        e.MaritalStatus = Clean(r.MaritalStatus);
        e.AccessForm = Clean(r.AccessForm);
        e.AccessGrantedOn = r.AccessGrantedOn;
        e.AccessIssuedBy = Clean(r.AccessIssuedBy);
        e.ContractStart = r.ContractStart;
        e.ContractTermMonths = r.ContractTermMonths;
        e.ContractEnd = r.ContractEnd ?? CalculateContractEnd(r.ContractStart, r.ContractTermMonths);
    }

    private static DateOnly? CalculateContractEnd(DateOnly? start, int? months) =>
        start is null || months is null ? null : start.Value.AddMonths(months.Value);

    private static string NormalizeName(string value)
    {
        var parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    private static string NormalizePersonnelNumber(string? value) =>
        string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.PersonnelNumber, e.LastName, e.FirstName, e.MiddleName, e.DateOfBirth,
        e.PassportSeries, e.PassportNumber, e.PassportIssuedBy, e.PassportIssuedOn,
        e.Position, e.Subdivision, e.Rank, e.MaritalStatus, e.AccessForm, e.AccessGrantedOn,
        e.AccessIssuedBy, e.ContractStart, e.ContractTermMonths, e.ContractEnd, e.IsArchived,
        e.UpdatedUtc, e.OrganizationalUnitId, e.OrganizationalUnit?.Name, e.MilitaryIdNumber,
        e.MilitaryRegistrationOffice, e.MilitaryRegistrationCategory, e.MilitaryRegistrationStatus,
        e.TransferDate, e.TransferFrom);

    private async Task AuditAsync(string action, string entityType, string entityId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ActorEmail = User.FindFirst(ClaimTypes.Email)?.Value,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync();
    }
}
