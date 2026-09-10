using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record CreateAuditLogRequest(AuditEntityType EntityType, Guid EntityId, string Action, string? Details);

[ApiController]
[Authorize]
[Route("api/audit-logs")]
public class AuditLogsController(ClinicAppDbContext db) : ControllerBase
{
    [Authorize(Roles = "Admin,Doctor,Staff")]
    [HttpGet]
    public async Task<ActionResult<List<AuditLog>>> GetAll(
        [FromQuery] AuditEntityType? entityType, [FromQuery] Guid? entityId,
        [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (entityType is not null) q = q.Where(a => a.EntityType == entityType);
        if (entityId is not null) q = q.Where(a => a.EntityId == entityId);
        return Ok(await q.OrderByDescending(a => a.PerformedAt).Take(take).ToListAsync(ct));
    }

    /// <summary>§16.2 — paged + searched audit trail for the admin screen.
    /// `q` matches action / details.</summary>
    [Authorize(Roles = "Admin,Doctor,Staff")]
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<AuditLog>>> Search(
        [FromQuery] string? q,
        [FromQuery] AuditEntityType? entityType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (entityType is not null) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(a => a.Action.Contains(s) || (a.Details != null && a.Details.Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.PerformedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResult<AuditLog> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    /// <summary>Written on consultation amend (contract §10, entity_type = Consultation).</summary>
    [Authorize(Roles = "Doctor,Admin,Staff")]
    [HttpPost]
    public async Task<ActionResult<AuditLog>> Create(CreateAuditLogRequest req, CancellationToken ct)
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var row = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = req.EntityType,
            EntityId = req.EntityId,
            Action = req.Action,
            Details = req.Details,
            PerformedByUserId = Guid.TryParse(sub, out var uid) ? uid : null,
            PerformedAt = DateTimeOffset.UtcNow
        };
        db.AuditLogs.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }
}
