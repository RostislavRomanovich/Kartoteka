using System.Security.Claims;
using System.Security.Cryptography;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Route("api/employees/{employeeId:long}/photo")]
[Authorize]
public sealed class EmployeePhotoController(AppDbContext db, IHttpContextAccessor accessor) : ControllerBase
{
    private const string PhotoCategory = "employee_photo";
    private const long MaxBytes = 5 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Get(long employeeId)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived)) return NotFound();
        var item = await db.EmployeeAttachments.AsNoTracking().FirstOrDefaultAsync(a =>
            a.EmployeeId == employeeId && a.Category == PhotoCategory && a.TargetType == "employee" && a.TargetId == 0);
        if (item is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "no-store";
        return File(item.Content, item.ContentType);
    }

    [HttpPut]
    [Authorize(Policy = "AdminOnly")]
    [RequestSizeLimit(MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
    public async Task<IActionResult> Put(long employeeId, IFormFile? file)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && !e.IsArchived)) return NotFound();
        if (file is null || file.Length <= 0) return BadRequest(new { error = "Выберите файл фотографии." });
        if (file.Length > MaxBytes) return BadRequest(new { error = "Максимальный размер фотографии — 5 МБ." });
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var bytes = stream.ToArray();
        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        var detected = DetectImageType(bytes);
        var expected = extension switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null };
        if (detected is null || expected != detected)
            return BadRequest(new { error = "Для фотографии разрешены только корректные JPG, PNG или WEBP-файлы с соответствующим расширением." });

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var existing = await db.EmployeeAttachments.Where(a => a.EmployeeId == employeeId && a.Category == PhotoCategory).OrderBy(a => a.Id).ToListAsync();
        var photo = existing.FirstOrDefault(x => x.Sha256 == hash) ?? existing.FirstOrDefault();
        if (photo is not null)
            foreach (var duplicate in existing.Where(x => x.Id != photo.Id)) db.EmployeeAttachments.Remove(duplicate);
        var safeName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "employee-photo" + extension;
        if (photo is null)
        {
            photo = new EmployeeAttachment { EmployeeId = employeeId, Category = PhotoCategory, TargetType = "employee", TargetId = 0 };
            db.EmployeeAttachments.Add(photo);
        }
        photo.OriginalFileName = safeName.Length > 255 ? safeName[..255] : safeName;
        photo.ContentType = detected;
        photo.Content = bytes;
        photo.Sha256 = hash;
        photo.UploadedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        photo.UploadedUtc = DateTime.UtcNow;
        photo.TargetType = "employee";
        photo.TargetId = 0;
        await db.SaveChangesAsync();
        await AuditAsync("employee_photo_saved", employeeId, photo.Id);
        return Ok(new { message = "Фотография сохранена." });
    }

    [HttpDelete]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(long employeeId)
    {
        var photos = await db.EmployeeAttachments.Where(a => a.EmployeeId == employeeId && a.Category == PhotoCategory).ToListAsync();
        if (photos.Count == 0) return NoContent();
        var ids = photos.Select(x => x.Id).ToArray();
        db.EmployeeAttachments.RemoveRange(photos);
        await db.SaveChangesAsync();
        foreach (var id in ids) await AuditAsync("employee_photo_deleted", employeeId, id);
        return NoContent();
    }

    private static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return "image/png";
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P') return "image/webp";
        return null;
    }

    private async Task AuditAsync(string action, long employeeId, long attachmentId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            ActorEmail = User.FindFirst(ClaimTypes.Email)?.Value,
            Action = action, EntityType = "employee_photo", EntityId = employeeId.ToString(),
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), Details = $"attachmentId={attachmentId}"
        });
        await db.SaveChangesAsync();
    }
}
