using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,editor,viewer")]
[Route("api/employees/{employeeId:long}/leave-entitlement")]
public sealed class EmployeeLeaveEntitlementsController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long employeeId, [FromQuery] int year, CancellationToken ct)
    {
        if (year < 2000 || year > 2200) return BadRequest(new { error = "Укажите корректный календарный год." });
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        var x = await db.EmployeeLeaveEntitlements.AsNoTracking().SingleOrDefaultAsync(v => v.EmployeeId == employeeId && v.CalendarYear == year, ct);
        var records = await db.EmployeeLeaveRecords.AsNoTracking().Where(v => v.EmployeeId == employeeId && v.Status != "cancelled" && v.ActualStart != null && v.ActualStart.Value.Year == year)
            .Select(v => new { v.LeaveType, Days = v.ActualDays ?? 0 }).ToListAsync(ct);
        var mainUsed = records.Where(v => v.LeaveType == "main").Sum(v => v.Days);
        var veteranUsed = records.Where(v => v.LeaveType == "veteran").Sum(v => v.Days);
        var additionalUsed = records.Where(v => v.LeaveType is "additional" or "other").Sum(v => v.Days);
        var main = x?.MainDays ?? 0; var veteran = x?.VeteranDays ?? 0; var additional = x?.AdditionalDays ?? 0;
        var carry = x?.CarryoverDays ?? 0; var adjustment = x?.ManualAdjustmentDays ?? 0;
        return Ok(new { employeeId, year, mainDays = main, veteranDays = veteran, additionalDays = additional, carryoverDays = carry, manualAdjustmentDays = adjustment,
            mainUsedDays = mainUsed, veteranUsedDays = veteranUsed, additionalUsedDays = additionalUsed,
            totalEntitledDays = main + veteran + additional + carry + adjustment,
            totalUsedDays = mainUsed + veteranUsed + additionalUsed,
            remainingDays = main + veteran + additional + carry + adjustment - mainUsed - veteranUsed - additionalUsed,
            basisNote = x?.BasisNote, configured = x is not null });
    }

    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Put(long employeeId, LeaveEntitlementRequest r, CancellationToken ct)
    {
        if (r.CalendarYear < 2000 || r.CalendarYear > 2200 || r.MainDays < 0 || r.VeteranDays < 0 || r.AdditionalDays < 0 || r.CarryoverDays < 0 || r.BasisNote?.Length > 1000)
            return BadRequest(new { error = "Проверьте год, количество суток и основание расчёта." });
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        var x = await db.EmployeeLeaveEntitlements.SingleOrDefaultAsync(v => v.EmployeeId == employeeId && v.CalendarYear == r.CalendarYear, ct);
        if (x is null) { x = new EmployeeLeaveEntitlement { EmployeeId = employeeId, CalendarYear = r.CalendarYear }; db.EmployeeLeaveEntitlements.Add(x); }
        x.MainDays = r.MainDays; x.VeteranDays = r.VeteranDays; x.AdditionalDays = r.AdditionalDays; x.CarryoverDays = r.CarryoverDays;
        x.ManualAdjustmentDays = r.ManualAdjustmentDays; x.BasisNote = Clean(r.BasisNote); x.UpdatedUtc = DateTime.UtcNow; x.UpdatedByUserId = ActorId();
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { ActorUserId = ActorId(), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = "leave_entitlement_updated", EntityType = "leave-entitlement", EntityId = x.Id.ToString(), IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(), Details = $"EmployeeId={employeeId}; Year={r.CalendarYear}" });
        await db.SaveChangesAsync(ct);
        return await Get(employeeId, r.CalendarYear, ct);
    }

    private string? ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class LeaveEntitlementRequest
{
    public int CalendarYear { get; set; }
    public int MainDays { get; set; }
    public int VeteranDays { get; set; }
    public int AdditionalDays { get; set; }
    public int CarryoverDays { get; set; }
    public int ManualAdjustmentDays { get; set; }
    public string? BasisNote { get; set; }
}
