using Kartoteka.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

public sealed record EmployeeAlertDto(
    string Id,
    string Type,
    string Severity,
    long EmployeeId,
    string EmployeeName,
    string Title,
    string Message,
    DateOnly DueDate,
    bool HighlightEmployeeName);

[ApiController]
[Route("api/alerts")]
[Authorize(Roles = "admin,editor,viewer")]
public sealed class AlertsController(AppDbContext db) : ControllerBase
{
    // Alerts are calculated from current records on every request. They are not stored,
    // so repeated requests cannot create duplicate notifications and corrected data clears them.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeAlertDto>>> Get(CancellationToken cancellationToken)
    {
        // Use the server's local calendar date because these are personnel date-only fields.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var licenseWindowEnd = today.AddDays(30);
        var contractWindowStart = today.AddMonths(6);
        var classnessWindowEnd = today.AddMonths(6);
        var birthdayWindowEnd = today.AddDays(7);
        var extendedData = await db.EmployeeExtendedData.AsNoTracking()
            .Where(x => x.UmoExpiresOn != null)
            .Select(x => new { x.EmployeeId, x.UmoExpiresOn })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.UmoExpiresOn, cancellationToken);

        var employees = await db.Employees
            .AsNoTracking()
            .Where(e => !e.IsArchived)
            .Include(e => e.LicenseCategories)
            .Include(e => e.ClassnessHistory)
            .Select(e => new AlertEmployeeData(
                e.Id, e.LastName, e.FirstName, e.MiddleName, e.DateOfBirth,
                e.ContractEnd, e.ContractStart, e.ContractTermMonths,
                e.LicenseCategories.Select(x => new AlertLicenseData(x.Id, x.Category, x.ExpiresOn)).ToList(),
                e.ClassnessHistory.Select(x => new AlertClassnessData(x.Id, x.Classness, x.ReviewDueOn)).ToList()))
            .ToListAsync(cancellationToken);

        var alerts = new List<EmployeeAlertDto>();
        foreach (var employee in employees)
        {
            var fio = string.Join(' ', new[] { employee.LastName, employee.FirstName, employee.MiddleName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            if (employee.DateOfBirth is not null)
            {
                var birthday = NextBirthday(employee.DateOfBirth.Value, today);
                if (birthday >= today && birthday <= birthdayWindowEnd)
                {
                    var days = birthday.DayNumber - today.DayNumber;
                    alerts.Add(new EmployeeAlertDto($"birthday:{employee.Id}:{birthday.Year}", "birthday", days == 0 ? "due" : "warning", employee.Id, fio,
                        "День рождения", days == 0 ? "Сегодня день рождения сотрудника." : $"День рождения через {days} дн. ({birthday:dd.MM.yyyy}).", birthday, false));
                }
            }

            if (extendedData.TryGetValue(employee.Id, out var umoExpiry) && umoExpiry is not null)
            {
                var expiry = umoExpiry.Value;
                var reminder = expiry.AddMonths(-1);
                if (today >= reminder)
                {
                    var severity = expiry < today ? "overdue" : expiry == today ? "due" : "warning";
                    alerts.Add(new EmployeeAlertDto($"umo:{employee.Id}", "umo", severity, employee.Id, fio, "Окончание срока УМО",
                        expiry < today ? $"Срок УМО истёк {expiry:dd.MM.yyyy}." : $"Срок УМО заканчивается {expiry:dd.MM.yyyy}.", expiry, true));
                }
            }

            var contractEnd = employee.ContractEnd;
            if (contractEnd is null && employee.ContractStart is not null && employee.ContractTermMonths is > 0)
                contractEnd = employee.ContractStart.Value.AddMonths(employee.ContractTermMonths.Value);

            if (contractEnd is not null)
            {
                // Reminder starts exactly six calendar months before the end date.
                var reminderDate = contractEnd.Value.AddMonths(-6);
                if (today >= reminderDate)
                {
                    var severity = contractEnd.Value < today ? "overdue" : contractEnd.Value == today ? "due" : "warning";
                    alerts.Add(new EmployeeAlertDto(
                        $"contract:{employee.Id}", "contract", severity, employee.Id, fio,
                        "Окончание контракта",
                        contractEnd.Value < today
                            ? $"Срок контракта истёк {contractEnd.Value:dd.MM.yyyy}. Проверьте сведения."
                            : $"Контракт заканчивается {contractEnd.Value:dd.MM.yyyy}.",
                        contractEnd.Value, true));
                }
            }

            foreach (var license in employee.Licenses)
            {
                if (license.ExpiresOn is null || license.ExpiresOn.Value > licenseWindowEnd)
                    continue;
                var expiry = license.ExpiresOn.Value;
                var severity = expiry < today ? "overdue" : expiry == today ? "due" : "warning";
                alerts.Add(new EmployeeAlertDto(
                    $"license:{license.Id}", "driving-license", severity, employee.Id, fio,
                    $"Срок действия водительской категории {license.Category}",
                    expiry < today
                        ? $"Срок действия категории истёк {expiry:dd.MM.yyyy}."
                        : $"Срок действия категории заканчивается {expiry:dd.MM.yyyy}.",
                    expiry, true));
            }

            foreach (var classness in employee.Classness)
            {
                if (classness.ReviewDueOn is null || classness.ReviewDueOn.Value > classnessWindowEnd)
                    continue;
                var due = classness.ReviewDueOn.Value;
                var severity = due < today ? "overdue" : due == today ? "due" : "warning";
                alerts.Add(new EmployeeAlertDto(
                    $"classness:{classness.Id}", "classness-review", severity, employee.Id, fio,
                    $"Проверка классности: {classness.Classness}",
                    due < today
                        ? $"Срок проверки классности истёк {due:dd.MM.yyyy}."
                        : $"Проверка классности назначена до {due:dd.MM.yyyy}.",
                    due, true));
            }
        }

        var result = alerts
            .OrderBy(a => a.DueDate)
            .ThenBy(a => a.EmployeeName)
            .ThenBy(a => a.Type)
            .ToArray();
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    private static DateOnly NextBirthday(DateOnly birthDate, DateOnly today)
    {
        var month = birthDate.Month; var day = birthDate.Day;
        if (month == 2 && day == 29 && !DateTime.IsLeapYear(today.Year)) day = 28;
        var next = new DateOnly(today.Year, month, day);
        if (next < today)
        {
            var year = today.Year + 1;
            if (month == 2 && day == 29 && !DateTime.IsLeapYear(year)) day = 28;
            else if (month == 2 && birthDate.Day == 29) day = 29;
            next = new DateOnly(year, month, day);
        }
        return next;
    }

    private sealed record AlertEmployeeData(
        long Id, string LastName, string FirstName, string? MiddleName, DateOnly? DateOfBirth,
        DateOnly? ContractEnd, DateOnly? ContractStart, int? ContractTermMonths,
        List<AlertLicenseData> Licenses, List<AlertClassnessData> Classness);
    private sealed record AlertLicenseData(long Id, string Category, DateOnly? ExpiresOn);
    private sealed record AlertClassnessData(long Id, string Classness, DateOnly? ReviewDueOn);
}
