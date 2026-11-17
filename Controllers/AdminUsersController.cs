using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/users")]
public sealed class AdminUsersController(
    UserManager<AppUser> users,
    AppDbContext db,
    IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.Ordinal)
        { "admin", "editor", "viewer" };

    public sealed record CreateUserRequest(
        [Required, EmailAddress, MaxLength(200)] string Email,
        [Required, MinLength(12), MaxLength(128)] string TemporaryPassword,
        [Required, MaxLength(200)] string DisplayName,
        [Required] string Role);

    public sealed record ChangeRoleRequest([Required] string Role);
    public sealed record SetActiveRequest(bool IsActive);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var allUsers = await users.Users.AsNoTracking().OrderBy(x => x.Email).ToListAsync();
        var result = new List<object>(allUsers.Count);
        foreach (var user in allUsers)
        {
            result.Add(new
            {
                user.Id, user.Email, user.DisplayName, user.IsActive, user.CreatedUtc,
                Roles = (await users.GetRolesAsync(user)).ToArray()
            });
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var role = NormalizeRole(request.Role);
        if (role is null) return BadRequest(new { error = "Роль должна быть admin, editor или viewer." });
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { error = "Укажите отображаемое имя." });

        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null)
            return Conflict(new { error = "Учётная запись с таким email уже существует." });

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };
        var created = await users.CreateAsync(user, request.TemporaryPassword);
        if (!created.Succeeded)
            return BadRequest(new { errors = created.Errors.Select(e => new { e.Code, e.Description }) });

        var roleResult = await users.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await users.DeleteAsync(user);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Не удалось назначить роль новой учётной записи." });
        }

        await AuditAsync("user_created", user.Id, $"role={role}");
        return Created($"/api/admin/users/{user.Id}", new
        {
            user.Id, user.Email, user.DisplayName, user.IsActive, Roles = new[] { role }
        });
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeRole(string id, ChangeRoleRequest request)
    {
        var targetRole = NormalizeRole(request.Role);
        if (targetRole is null) return BadRequest(new { error = "Роль должна быть admin, editor или viewer." });
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();

        var currentRoles = await users.GetRolesAsync(user);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId && !string.Equals(targetRole, "admin", StringComparison.Ordinal))
            return Conflict(new { error = "Нельзя снять роль admin с собственной активной сессии." });

        if (currentRoles.Contains("admin") && targetRole != "admin" && user.IsActive &&
            await CountActiveAdminsAsync() <= 1)
            return Conflict(new { error = "Нельзя снять роль у последнего активного администратора." });

        var removed = await users.RemoveFromRolesAsync(user, currentRoles);
        if (!removed.Succeeded) return StatusCode(500, new { error = "Не удалось удалить прежние роли." });
        var added = await users.AddToRoleAsync(user, targetRole);
        if (!added.Succeeded)
        {
            // Best-effort rollback so a failed role change does not leave an account roleless.
            if (currentRoles.Count > 0) await users.AddToRolesAsync(user, currentRoles);
            return StatusCode(500, new { error = "Не удалось назначить новую роль." });
        }

        await AuditAsync("user_role_changed", user.Id,
            $"from={string.Join(",", currentRoles)};to={targetRole}");
        return Ok(new { user.Id, user.Email, Role = targetRole });
    }

    [HttpPut("{id}/active")]
    public async Task<IActionResult> SetActive(string id, SetActiveRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId && !request.IsActive)
            return Conflict(new { error = "Нельзя деактивировать собственную учётную запись." });

        var roles = await users.GetRolesAsync(user);
        if (!request.IsActive && user.IsActive && roles.Contains("admin") && await CountActiveAdminsAsync() <= 1)
            return Conflict(new { error = "Нельзя деактивировать последнего активного администратора." });

        user.IsActive = request.IsActive;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded) return StatusCode(500, new { error = "Не удалось обновить состояние учётной записи." });
        await AuditAsync(request.IsActive ? "user_activated" : "user_deactivated", user.Id, null);
        return Ok(new { user.Id, user.Email, user.IsActive });
    }

    private string? NormalizeRole(string? role)
    {
        var normalized = role?.Trim().ToLowerInvariant();
        return normalized is not null && AllowedRoles.Contains(normalized) ? normalized : null;
    }

    private async Task<int> CountActiveAdminsAsync()
    {
        var admins = await users.GetUsersInRoleAsync("admin");
        return admins.Count(x => x.IsActive);
    }

    private async Task AuditAsync(string action, string targetUserId, string? details)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            ActorEmail = User.FindFirstValue(ClaimTypes.Email),
            Action = action,
            EntityType = "user",
            EntityId = targetUserId,
            IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Details = details
        });
        await db.SaveChangesAsync();
    }
}
