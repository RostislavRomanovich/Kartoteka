using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/employees/{employeeId:long}/extended-data")]
public sealed class EmployeeExtendedDataController(AppDbContext db, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long employeeId, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        var data = await db.EmployeeExtendedData.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        return Ok(data ?? new EmployeeExtendedData { EmployeeId = employeeId });
    }

    [HttpPut]
    public async Task<IActionResult> Put(long employeeId, ExtendedDataRequest r, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived, ct)) return NotFound();
        var validation = Validate(r);
        if (validation is not null) return BadRequest(new { error = validation });
        var x = await db.EmployeeExtendedData.SingleOrDefaultAsync(v => v.EmployeeId == employeeId, ct);
        if (x is null) { x = new EmployeeExtendedData { EmployeeId = employeeId }; db.EmployeeExtendedData.Add(x); }
        x.Inn = Clean(r.Inn); x.Snils = Clean(r.Snils); x.PekNumber = Clean(r.PekNumber);
        x.DactyloscopyPerformed = r.DactyloscopyPerformed; x.DactyloscopyDate = r.DactyloscopyDate;
        x.BloodGroup = Clean(r.BloodGroup); x.RhFactor = Clean(r.RhFactor);
        x.DnaTestPerformed = r.DnaTestPerformed; x.DnaTestDate = r.DnaTestDate;
        x.UmoPassedOn = r.UmoPassedOn; x.UmoExpiresOn = r.UmoExpiresOn;
        x.FitnessCategory = Clean(r.FitnessCategory); x.HeightCm = r.HeightCm; x.WeightKg = r.WeightKg;
        x.ClothingSize = Clean(r.ClothingSize); x.ShoeSize = Clean(r.ShoeSize);
        x.DrivingLicenseSeries = Clean(r.DrivingLicenseSeries); x.DrivingLicenseNumber = Clean(r.DrivingLicenseNumber);
        x.DrivingLicenseIssuedBy = Clean(r.DrivingLicenseIssuedBy); x.DrivingLicenseIssuedOn = r.DrivingLicenseIssuedOn;
        x.UpdatedUtc = DateTime.UtcNow; x.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        await Audit("employee_extended_data_updated", employeeId, ct);
        return Ok(x);
    }

    private static string? Validate(ExtendedDataRequest r)
    {
        if (r.DactyloscopyPerformed == false && r.DactyloscopyDate is not null)
            return "Дата дактилоскопии указана, хотя признак проведения установлен в значение «Нет».";
        if (r.DnaTestPerformed == false && r.DnaTestDate is not null)
            return "Дата ДНК-теста указана, хотя признак проведения установлен в значение «Нет».";
        if (r.UmoExpiresOn is not null && r.UmoPassedOn is not null && r.UmoExpiresOn < r.UmoPassedOn)
            return "Дата окончания УМО не может быть раньше даты прохождения.";
        if (r.HeightCm is not null && (r.HeightCm < 30 || r.HeightCm > 260)) return "Рост должен быть в диапазоне 30–260 см.";
        if (r.WeightKg is not null && (r.WeightKg < 1 || r.WeightKg > 500)) return "Вес должен быть в диапазоне 1–500 кг.";
        if (r.BloodGroup is not null && r.BloodGroup is not ("I" or "II" or "III" or "IV" or "0" or "A" or "B" or "AB")) return "Недопустимое значение группы крови.";
        if (r.RhFactor is not null && r.RhFactor is not ("+" or "-" or "Положительный" or "Отрицательный")) return "Резус-фактор должен быть положительным или отрицательным.";
        return null;
    }
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private async Task Audit(string action, long employeeId, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog { ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), ActorEmail = User.FindFirstValue(ClaimTypes.Email), Action = action, EntityType = "employee-extended-data", EntityId = employeeId.ToString(), IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString() });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ExtendedDataRequest
{
    public string? Inn { get; set; }
    public string? Snils { get; set; }
    public string? PekNumber { get; set; }
    public bool? DactyloscopyPerformed { get; set; }
    public DateOnly? DactyloscopyDate { get; set; }
    public string? BloodGroup { get; set; }
    public string? RhFactor { get; set; }
    public bool? DnaTestPerformed { get; set; }
    public DateOnly? DnaTestDate { get; set; }
    public DateOnly? UmoPassedOn { get; set; }
    public DateOnly? UmoExpiresOn { get; set; }
    public string? FitnessCategory { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? ClothingSize { get; set; }
    public string? ShoeSize { get; set; }
    public string? DrivingLicenseSeries { get; set; }
    public string? DrivingLicenseNumber { get; set; }
    public string? DrivingLicenseIssuedBy { get; set; }
    public DateOnly? DrivingLicenseIssuedOn { get; set; }
}
