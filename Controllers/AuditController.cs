using Kartoteka.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kartoteka.Server.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/audit")]
public sealed class AuditController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] string? action,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Clamp(page, 1, 1000000);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => (x.ActorEmail != null && x.ActorEmail.Contains(term)) ||
                x.Action.Contains(term) || x.EntityType.Contains(term) ||
                (x.EntityId != null && x.EntityId.Contains(term)) ||
                (x.Details != null && x.Details.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action.Trim());
        if (fromUtc.HasValue) query = query.Where(x => x.OccurredUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.OccurredUtc < toUtc.Value);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.OccurredUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.OccurredUtc, x.ActorEmail, x.Action, x.EntityType,
                x.EntityId, x.IpAddress, x.Details
            }).ToListAsync();
        return Ok(new { page, pageSize, total, items });
    }
}
