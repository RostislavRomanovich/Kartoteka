using System.Security.Cryptography;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/employees/{employeeId:long}/attachments")]
public sealed class AttachmentsController(AppDbContext db, IHttpContextAccessor accessor) : ControllerBase
{
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.Ordinal)
    {
        "employee", "relative", "car", "license-category", "position-history",
        "classness", "award", "trip", "messenger"
    };
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedGroups = new(StringComparer.Ordinal)
    {
        ["passport"] = "Паспорт",
        ["military_id"] = "Военный билет",
        ["access"] = "Документы допуска",
        ["contract"] = "Контракт",
        ["driving_license"] = "Водительское удостоверение",
        ["car_registration"] = "Документы на автомобиль",
        ["birth_certificate"] = "Свидетельство о рождении",
        ["award"] = "Наградные документы",
        ["personal_feature"] = "Фотографии особых примет",
        ["relative"] = "Документы родственников",
        ["other"] = "Прочие документы"
    };

    [HttpGet]
    public async Task<IActionResult> List(long employeeId, [FromQuery] string? category = null,
        [FromQuery] string? targetType = null, [FromQuery] long? targetId = null)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        var query = db.EmployeeAttachments.AsNoTracking().Where(a => a.EmployeeId == employeeId && a.Category != "employee_photo");
        if (!string.IsNullOrWhiteSpace(targetType))
        {
            targetType = targetType.Trim().ToLowerInvariant();
            if (!AllowedTargets.Contains(targetType)) return BadRequest(new { error = "Неизвестный тип записи для вложения." });
            query = query.Where(a => a.TargetType == targetType);
            if (targetId.HasValue) query = query.Where(a => a.TargetId == targetId.Value);
        }
        else if (targetId.HasValue) return BadRequest(new { error = "Для targetId требуется targetType." });
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!AllowedGroups.ContainsKey(category)) return BadRequest(new { error = "Неизвестная группа документов." });
            query = query.Where(a => a.Category == category);
        }
        var items = await query.OrderBy(a => a.Category).ThenBy(a => a.OriginalFileName)
            .Select(a => new { a.Id, a.OriginalFileName, a.ContentType, a.Category, a.TargetType, a.TargetId, a.UploadedUtc, a.Sha256 })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> Upload(long employeeId, IFormFile? file, [FromQuery] string category = "other",
        [FromQuery] string targetType = "employee", [FromQuery] long targetId = 0)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived)) return NotFound();
        if (!AllowedGroups.ContainsKey(category)) return BadRequest(new { error = "Неизвестная группа документов." });
        targetType = (targetType ?? "employee").Trim().ToLowerInvariant();
        if (!AllowedTargets.Contains(targetType) || targetId < 0)
            return BadRequest(new { error = "Некорректная привязка вложения." });
        if (!await TargetBelongsToEmployee(employeeId, targetType, targetId))
            return BadRequest(new { error = "Выбранная запись не принадлежит этому сотруднику или не существует." });
        if (file is null || file.Length <= 0) return BadRequest(new { error = "Файл пуст." });
        if (file.Length > MaxBytes) return BadRequest(new { error = "Максимальный размер файла — 10 МБ." });

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var bytes = stream.ToArray();
        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        var detected = DetectSafeContentType(bytes);
        var expected = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (detected is null || expected != detected)
            return BadRequest(new { error = "Разрешены только корректные PDF, JPG, PNG и WEBP-файлы. Расширение должно соответствовать содержимому." });

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (await db.EmployeeAttachments.AnyAsync(a =>
                a.EmployeeId == employeeId && a.Category == category && a.TargetType == targetType &&
                a.TargetId == targetId && a.Sha256 == hash))
            return Conflict(new { error = "Этот файл уже прикреплён к данной группе документов." });

        var safeName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "document" + extension;
        var attachment = new EmployeeAttachment
        {
            EmployeeId = employeeId,
            OriginalFileName = safeName.Length > 255 ? safeName[..255] : safeName,
            ContentType = detected,
            Category = category,
            TargetType = targetType,
            TargetId = targetId,
            Content = bytes,
            Sha256 = hash,
            UploadedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        };
        db.EmployeeAttachments.Add(attachment);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "Этот файл уже прикреплён к данной группе документов." }); }
        await AuditAsync("attachment_uploaded", employeeId, attachment.Id);
        return CreatedAtAction(nameof(Download), new { employeeId, attachmentId = attachment.Id },
            new { attachment.Id, attachment.OriginalFileName, attachment.Category, attachment.TargetType, attachment.TargetId, attachment.Sha256 });
    }

    [HttpGet("{attachmentId:long}")]
    public async Task<IActionResult> Download(long employeeId, long attachmentId)
    {
        var item = await db.EmployeeAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Id == attachmentId);
        if (item is null || item.Category == "employee_photo") return NotFound();
        await AuditAsync("attachment_downloaded", employeeId, attachmentId);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(item.Content, item.ContentType, item.OriginalFileName);
    }

    [HttpDelete("{attachmentId:long}")]
    public async Task<IActionResult> Delete(long employeeId, long attachmentId)
    {
        var item = await db.EmployeeAttachments
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Id == attachmentId);
        if (item is null || item.Category == "employee_photo") return NotFound();
        db.EmployeeAttachments.Remove(item);
        await db.SaveChangesAsync();
        await AuditAsync("attachment_deleted", employeeId, attachmentId);
        return NoContent();
    }

    private async Task<bool> TargetBelongsToEmployee(long employeeId, string targetType, long targetId)
    {
        if (targetType == "employee") return targetId == 0 &&
            await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived);
        if (targetId <= 0) return false;
        return targetType switch
        {
            "relative" => await db.Relatives.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "car" => await db.EmployeeCars.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "license-category" => await db.DrivingLicenseCategories.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "position-history" => await db.PositionHistories.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "classness" => await db.ClassnessHistories.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "award" => await db.Awards.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "trip" => await db.BusinessTrips.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            "messenger" => await db.EmployeeMessengers.AnyAsync(x => x.Id == targetId && x.EmployeeId == employeeId),
            _ => false
        };
    }

    private static string? DetectSafeContentType(byte[] bytes)
    {
        if (bytes.Length >= 5 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P' &&
            bytes[2] == (byte)'D' && bytes[3] == (byte)'F' && bytes[4] == (byte)'-')
            return "application/pdf";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E &&
            bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' &&
            bytes[2] == (byte)'F' && bytes[3] == (byte)'F' && bytes[8] == (byte)'W' &&
            bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return "image/webp";
        return null;
    }

    private async Task AuditAsync(string action, long employeeId, long attachmentId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            ActorEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            Action = action,
            EntityType = "attachment",
            EntityId = attachmentId.ToString(),
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Details = $"employeeId={employeeId}"
        });
        await db.SaveChangesAsync();
    }
}
