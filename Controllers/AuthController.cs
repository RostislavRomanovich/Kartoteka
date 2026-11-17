using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signInManager,
    IConfiguration configuration,
    AppDbContext db) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record LoginResponse(string AccessToken, DateTime ExpiresUtc, string[] Roles);

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            await AuditAsync(null, request.Email, "login_failed", "user", null, "Unknown or inactive account");
            return Unauthorized(new { error = "Неверные учётные данные." });
        }

        var check = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            await AuditAsync(user.Id, user.Email, "login_failed", "user", user.Id, "Password validation failed");
            return Unauthorized(new { error = "Неверные учётные данные." });
        }

        var roles = (await users.GetRolesAsync(user)).ToArray();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var keyText = Environment.GetEnvironmentVariable("KARTOTEKA_JWT_SIGNING_KEY")
            ?? configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT key is missing.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(20);
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "Kartoteka.Server",
            audience: configuration["Jwt:Audience"] ?? "Kartoteka.Client",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        await AuditAsync(user.Id, user.Email, "login_succeeded", "user", user.Id, null);
        return Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, roles));
    }

    private async Task AuditAsync(string? userId, string? email, string action, string entityType,
        string? entityId, string? details)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            ActorEmail = email,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Details = details
        });
        await db.SaveChangesAsync();
    }
}
