using System.ComponentModel.DataAnnotations;
using Kartoteka.Server.Data;
using Kartoteka.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kartoteka.Server.Controllers;

public sealed record OrganizationalUnitDto(long Id, string Name, long? ParentUnitId, bool IsActive, int SortOrder);

public sealed class SaveOrganizationalUnitRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public long? ParentUnitId { get; set; }
    public bool IsActive { get; set; } = true;
    [Range(0, 100000)] public int SortOrder { get; set; }
}

[ApiController]
[Authorize]
[Route("api/organizational-units")]
public sealed class OrganizationalUnitsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationalUnitDto>>> List([FromQuery] bool includeInactive = false)
    {
        if (includeInactive && !User.IsInRole("admin")) return Forbid();
        var query = db.OrganizationalUnits.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        var units = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync();
        return Ok(units.Select(ToDto).ToList());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<OrganizationalUnitDto>> Get(long id)
    {
        var unit = await db.OrganizationalUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (unit is null || (!unit.IsActive && !User.IsInRole("admin"))) return NotFound();
        return Ok(ToDto(unit));
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OrganizationalUnitDto>> Create(SaveOrganizationalUnitRequest request)
    {
        var error = await ValidateAsync(request, null);
        if (error is not null) return BadRequest(new { error });
        var unit = new OrganizationalUnit { Name = request.Name.Trim(), ParentUnitId = request.ParentUnitId,
            IsActive = request.IsActive, SortOrder = request.SortOrder };
        db.OrganizationalUnits.Add(unit);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "Подразделение с таким названием уже существует на этом уровне." }); }
        return CreatedAtAction(nameof(Get), new { id = unit.Id }, ToDto(unit));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<OrganizationalUnitDto>> Update(long id, SaveOrganizationalUnitRequest request)
    {
        var unit = await db.OrganizationalUnits.FirstOrDefaultAsync(x => x.Id == id);
        if (unit is null) return NotFound();
        var error = await ValidateAsync(request, id);
        if (error is not null) return BadRequest(new { error });
        if (!request.IsActive && await db.Employees.AnyAsync(e => e.OrganizationalUnitId == id && !e.IsArchived))
            return Conflict(new { error = "Нельзя деактивировать подразделение, пока к нему привязаны действующие сотрудники." });
        if (!request.IsActive && await db.OrganizationalUnits.AnyAsync(u => u.ParentUnitId == id && u.IsActive))
            return Conflict(new { error = "Сначала деактивируйте или переместите дочерние подразделения." });
        unit.Name = request.Name.Trim();
        unit.ParentUnitId = request.ParentUnitId;
        unit.IsActive = request.IsActive;
        unit.SortOrder = request.SortOrder;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "Подразделение с таким названием уже существует на этом уровне." }); }
        return Ok(ToDto(unit));
    }

    private async Task<string?> ValidateAsync(SaveOrganizationalUnitRequest r, long? currentId)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Название подразделения обязательно.";
        if (r.ParentUnitId is null) return null;
        if (currentId == r.ParentUnitId) return "Подразделение не может быть родителем самого себя.";
        var parent = await db.OrganizationalUnits.FirstOrDefaultAsync(x => x.Id == r.ParentUnitId && x.IsActive);
        if (parent is null) return "Родительское подразделение не найдено или неактивно.";

        // Prevent cycles by walking the parent chain upward from the proposed parent.
        var seen = new HashSet<long>();
        var cursor = parent;
        while (true)
        {
            if (currentId is not null && cursor.Id == currentId.Value)
                return "Нельзя переместить подразделение внутрь собственного дочернего подразделения.";
            if (!seen.Add(cursor.Id)) return "В существующей иерархии обнаружен цикл; сначала исправьте данные.";
            if (cursor.ParentUnitId is null) break;
            var next = await db.OrganizationalUnits.FirstOrDefaultAsync(x => x.Id == cursor.ParentUnitId.Value);
            if (next is null) break;
            cursor = next;
        }
        return null;
    }

    private static OrganizationalUnitDto ToDto(OrganizationalUnit u) =>
        new(u.Id, u.Name, u.ParentUnitId, u.IsActive, u.SortOrder);
}
