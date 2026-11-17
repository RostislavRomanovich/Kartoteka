using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

// Deliberately separate from ordinary employee endpoints: only administrators may access this data.
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/employees/{employeeId:long}/confidential-health")]
public sealed class EmployeeConfidentialHealthController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long employeeId, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        var x = await db.EmployeeConfidentialHealthRecords.AsNoTracking().SingleOrDefaultAsync(v => v.EmployeeId == employeeId, ct);
        return Ok(x ?? new EmployeeConfidentialHealthRecord { EmployeeId = employeeId });
    }

    [HttpPut]
    public async Task<IActionResult> Put(long employeeId, ConfidentialHealthRequest r, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        if (r.RestrictedNote?.Length > 1000 || r.PsychiatricFitnessCategory?.Length > 100 ||
            (r.PsychiatricAssessmentDate is not null && r.PsychiatricAssessmentDate > DateOnly.FromDateTime(DateTime.Today)))
            return BadRequest(new { error = "Проверьте значения и даты ограниченных медицинских сведений." });
        var x = await db.EmployeeConfidentialHealthRecords.SingleOrDefaultAsync(v => v.EmployeeId == employeeId, ct);
        if (x is null) { x = new EmployeeConfidentialHealthRecord { EmployeeId = employeeId }; db.EmployeeConfidentialHealthRecords.Add(x); }
        x.PsychiatricFitnessCategory = Clean(r.PsychiatricFitnessCategory);
        x.PsychiatricAssessmentDate = r.PsychiatricAssessmentDate;
        x.RestrictedNote = Clean(r.RestrictedNote);
        x.UpdatedUtc = DateTime.UtcNow; x.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = "confidential_health_updated", EntityType = "confidential-health", EntityId = employeeId.ToString(), IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(), Details = "Изменены ограниченные медицинские сведения" });
        await db.SaveChangesAsync(ct);
        return Ok(new { x.EmployeeId, x.PsychiatricFitnessCategory, x.PsychiatricAssessmentDate, x.UpdatedUtc });
    }
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class ConfidentialHealthRequest
{
    public string? PsychiatricFitnessCategory { get; set; }
    public DateOnly? PsychiatricAssessmentDate { get; set; }
    public string? RestrictedNote { get; set; }
}
