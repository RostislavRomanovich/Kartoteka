using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,editor,viewer")]
[Route("api/employees/{employeeId:long}/leaves")]
public sealed class EmployeeLeavesController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(long employeeId, [FromQuery] int? year, CancellationToken ct)
    {
        if (!await ActiveEmployee(employeeId, ct)) return NotFound();
        var q = db.EmployeeLeaveRecords.AsNoTracking().Where(x => x.EmployeeId == employeeId);
        if (year is not null) q = q.Where(x => x.PlannedStart.Year == year || (x.ActualStart != null && x.ActualStart.Value.Year == year));
        return Ok(await q.OrderBy(x => x.PlannedStart).ToListAsync(ct));
    }

    [HttpPost]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> Create(long employeeId, LeaveRecordRequest r, CancellationToken ct)
    {
        if (!await ActiveEmployee(employeeId, ct)) return NotFound();
        var error = Validate(r); if (error is not null) return BadRequest(new { error });
        var x = new EmployeeLeaveRecord { EmployeeId = employeeId };
        Apply(x, r); x.CreatedUtc = x.UpdatedUtc = DateTime.UtcNow; x.UpdatedByUserId = ActorId();
        db.EmployeeLeaveRecords.Add(x); await db.SaveChangesAsync(ct); await Audit("leave_created", employeeId, x.Id, ct);
        return Created($"/api/employees/{employeeId}/leaves/{x.Id}", x);
    }

    [HttpPut("{leaveId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> Update(long employeeId, long leaveId, LeaveRecordRequest r, CancellationToken ct)
    {
        var x = await db.EmployeeLeaveRecords.SingleOrDefaultAsync(v => v.Id == leaveId && v.EmployeeId == employeeId, ct);
        if (x is null) return NotFound();
        var error = Validate(r); if (error is not null) return BadRequest(new { error });
        Apply(x, r); x.UpdatedUtc = DateTime.UtcNow; x.UpdatedByUserId = ActorId();
        await db.SaveChangesAsync(ct); await Audit("leave_updated", employeeId, x.Id, ct);
        return Ok(x);
    }

    [HttpDelete("{leaveId:long}")]
    [Authorize(Policy = "CanEditEmployees")]
    public async Task<IActionResult> Delete(long employeeId, long leaveId, CancellationToken ct)
    {
        var x = await db.EmployeeLeaveRecords.SingleOrDefaultAsync(v => v.Id == leaveId && v.EmployeeId == employeeId, ct);
        if (x is null) return NotFound();
        x.Status = "cancelled"; x.UpdatedUtc = DateTime.UtcNow; x.UpdatedByUserId = ActorId();
        await db.SaveChangesAsync(ct); await Audit("leave_cancelled", employeeId, x.Id, ct);
        return NoContent();
    }

    private async Task<bool> ActiveEmployee(long id, CancellationToken ct) => await db.Employees.AnyAsync(e => e.Id == id && !e.IsArchived, ct);
    private static string? Validate(LeaveRecordRequest r)
    {
        if (r.LeaveType is not ("main" or "veteran" or "additional" or "other")) return "Неизвестный вид отпуска.";
        if (r.Status is not ("planned" or "in-progress" or "completed" or "cancelled")) return "Неизвестный статус отпуска.";
        if (r.PlannedStart == default || r.PlannedEnd == default) return "Укажите обе плановые даты отпуска.";
        if (r.PlannedEnd < r.PlannedStart) return "Плановая дата окончания не может быть раньше даты начала.";
        if (r.Status == "completed" && (r.ActualStart is null || r.ActualEnd is null)) return "Для завершённого отпуска укажите фактические даты.";
        if (r.PlannedDaysOverride is <= 0 || r.ActualDaysOverride is < 0) return "Количество суток отпуска указано некорректно.";
        if (r.ActualStart is not null && r.ActualEnd is not null && r.ActualEnd < r.ActualStart) return "Фактическая дата окончания не может быть раньше даты начала.";
        if (r.Note?.Length > 1000) return "Примечание не должно превышать 1000 символов.";
        return null;
    }
    private static void Apply(EmployeeLeaveRecord x, LeaveRecordRequest r)
    {
        x.LeaveType = r.LeaveType; x.PlannedStart = r.PlannedStart; x.PlannedEnd = r.PlannedEnd;
        x.PlannedDays = r.PlannedDaysOverride ?? (r.PlannedEnd.DayNumber - r.PlannedStart.DayNumber + 1);
        x.ActualStart = r.ActualStart; x.ActualEnd = r.ActualEnd;
        x.ActualDays = r.ActualDaysOverride ?? (r.ActualStart is not null && r.ActualEnd is not null ? r.ActualEnd.Value.DayNumber - r.ActualStart.Value.DayNumber + 1 : null);
        x.Status = r.Status; x.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
    }
    private string? ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private async Task Audit(string action, long employeeId, long id, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog { ActorUserId = ActorId(), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = action, EntityType = "leave", EntityId = id.ToString(), Details = $"EmployeeId={employeeId}", IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString() });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class LeaveRecordRequest
{
    public string LeaveType { get; set; } = "main";
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public int? PlannedDaysOverride { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public int? ActualDaysOverride { get; set; }
    public string Status { get; set; } = "planned";
    public string? Note { get; set; }
}
