using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,editor,viewer")]
[Route("api/employees/{employeeId:long}/leave-preferences")]
public sealed class LeavePreferencesController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long employeeId, [FromQuery] int year, CancellationToken ct)
    {
        if (year < 2000 || year > 2200) return BadRequest(new { error = "Укажите календарный год." });
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        return Ok(await db.EmployeeLeavePreferences.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.CalendarYear == year).OrderBy(x => x.Priority).ToListAsync(ct));
    }

    [HttpPut]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> Put(long employeeId, LeavePreferenceRequest r, CancellationToken ct)
    {
        if (r.CalendarYear < 2000 || r.CalendarYear > 2200 || r.Preferences.Count > 12 || r.Preferences.Any(x => x.Month is < 1 or > 12 || x.Priority is < 1 or > 12) || r.Preferences.Select(x => x.Month).Distinct().Count() != r.Preferences.Count)
            return BadRequest(new { error = "Укажите уникальные месяцы от 1 до 12 и приоритеты от 1 до 12." });
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        var old = await db.EmployeeLeavePreferences.Where(x => x.EmployeeId == employeeId && x.CalendarYear == r.CalendarYear).ToListAsync(ct);
        db.EmployeeLeavePreferences.RemoveRange(old);
        foreach (var pref in r.Preferences) db.EmployeeLeavePreferences.Add(new EmployeeLeavePreference { EmployeeId = employeeId, CalendarYear = r.CalendarYear, Month = pref.Month, Priority = pref.Priority, Note = string.IsNullOrWhiteSpace(pref.Note) ? null : pref.Note.Trim() });
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = "leave_preferences_updated", EntityType = "employee", EntityId = employeeId.ToString(), IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(), Details = $"CalendarYear={r.CalendarYear}" });
        await db.SaveChangesAsync(ct);
        return await Get(employeeId, r.CalendarYear, ct);
    }
}

public sealed class LeavePreferenceRequest
{
    public int CalendarYear { get; set; }
    public List<LeaveMonthPreference> Preferences { get; set; } = [];
}
public sealed class LeaveMonthPreference { public int Month { get; set; } public int Priority { get; set; } public string? Note { get; set; } }

[ApiController]
[Authorize(Roles = "admin,editor,viewer")]
[Route("api/leave-blackouts")]
public sealed class LeaveBlackoutController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? year, CancellationToken ct)
    {
        var q = db.LeaveBlackoutPeriods.AsNoTracking().Where(x => x.IsActive);
        if (year is not null) q = q.Where(x => x.StartDate.Year <= year && x.EndDate.Year >= year);
        return Ok(await q.OrderBy(x => x.StartDate).Select(x => new { x.Id, x.StartDate, x.EndDate, x.OrganizationalUnitId, UnitName = x.OrganizationalUnit == null ? null : x.OrganizationalUnit.Name, x.Reason }).ToListAsync(ct));
    }
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create(LeaveBlackoutRequest r, CancellationToken ct)
    {
        if (r.EndDate < r.StartDate || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length > 300) return BadRequest(new { error = "Проверьте период и причину запрета." });
        if (r.OrganizationalUnitId is not null && !await db.OrganizationalUnits.AnyAsync(x => x.Id == r.OrganizationalUnitId && x.IsActive, ct)) return BadRequest(new { error = "Подразделение не найдено или неактивно." });
        var x = new LeaveBlackoutPeriod { StartDate = r.StartDate, EndDate = r.EndDate, OrganizationalUnitId = r.OrganizationalUnitId, Reason = r.Reason.Trim(), CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) };
        db.LeaveBlackoutPeriods.Add(x); await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = "leave_blackout_created", EntityType = "leave-blackout", EntityId = x.Id.ToString(), IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString() }); await db.SaveChangesAsync(ct);
        return Created($"/api/leave-blackouts/{x.Id}", x);
    }
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Deactivate(long id, CancellationToken ct)
    {
        var x = await db.LeaveBlackoutPeriods.SingleOrDefaultAsync(v => v.Id == id, ct); if (x is null) return NotFound();
        x.IsActive = false; await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = "leave_blackout_deactivated", EntityType = "leave-blackout", EntityId = id.ToString(), IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString() }); await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
public sealed class LeaveBlackoutRequest { public DateOnly StartDate { get; set; } public DateOnly EndDate { get; set; } public long? OrganizationalUnitId { get; set; } public string Reason { get; set; } = ""; }
