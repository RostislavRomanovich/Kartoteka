using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Allows the same published application to run interactively during development
// and under the Windows Service Control Manager after installation.
builder.Host.UseWindowsService(options => options.ServiceName = "Kartoteka Server");

var connectionString = Environment.GetEnvironmentVariable("KARTOTEKA_DB_CONNECTION")
    ?? builder.Configuration.GetConnectionString("Kartoteka")
    ?? throw new InvalidOperationException("Не задана строка подключения PostgreSQL.");

var jwtKey = Environment.GetEnvironmentVariable("KARTOTEKA_JWT_SIGNING_KEY")
    ?? builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32 ||
    jwtKey.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Задайте KARTOTEKA_JWT_SIGNING_KEY: случайный секрет длиной не менее 32 символов.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Kartoteka.Server";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Kartoteka.Client";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanEditEmployees", p => p.RequireRole("admin", "editor"));
    options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
    options.AddPolicy("CanExport", p => p.RequireRole("admin", "editor", "viewer"));
});
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

// Bootstrap is deliberately environment-driven: no default credentials are shipped.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var db = services.GetRequiredService<AppDbContext>();

    // Only the installer may set this flag for a brand-new, empty database.
    // EnsureCreated is not a schema-upgrade mechanism; existing databases are never altered here.
    var initializeDatabase = string.Equals(
        Environment.GetEnvironmentVariable("KARTOTEKA_INITIALIZE_DATABASE"), "true",
        StringComparison.OrdinalIgnoreCase);
    if (initializeDatabase)
    await db.Database.MigrateAsync();

    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException("Не удаётся подключиться к PostgreSQL. Проверьте KARTOTEKA_DB_CONNECTION.");
    foreach (var role in new[] { "admin", "editor", "viewer" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    var email = Environment.GetEnvironmentVariable("KARTOTEKA_BOOTSTRAP_ADMIN_EMAIL");
    var password = Environment.GetEnvironmentVariable("KARTOTEKA_BOOTSTRAP_ADMIN_PASSWORD");
    if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
        {
            var admin = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Первый администратор",
                IsActive = true
            };
            var result = await userManager.CreateAsync(admin, password);

if (!result.Succeeded)
{
    throw new InvalidOperationException(
        "Admin creation failed: " +
        string.Join("; ", result.Errors.Select(e => e.Code)));
}

var roleResult = await userManager.AddToRoleAsync(admin, "admin");

if (!roleResult.Succeeded)
{
    throw new InvalidOperationException(
        "Admin role assignment failed: " +
        string.Join("; ", roleResult.Errors.Select(e => e.Code)));
}
        }
        else if (!await userManager.IsInRoleAsync(existing, "admin"))
        {
            throw new InvalidOperationException(
                "Указанная учётная запись уже существует, но не имеет роль admin. " +
                "Назначьте роль отдельной проверенной административной процедурой.");
        }
    }
}

app.Run();

public partial class Program { }
