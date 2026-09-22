using ClinicApp.Api.Security;
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
public class AuditLogsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    /// <summary>RLS `audit_logs_select_admin`: the audit trail is Admin-only — it carries
    /// before/after patient field values. The one exception is the amend history a doctor sees
    /// on the consultation page, so a Doctor may read `Consultation` entries only.</summary>
    [Authorize(Roles = "Admin,Doctor")]
    [HttpGet]
    public async Task<ActionResult<List<AuditLog>>> GetAll(
        [FromQuery] AuditEntityType? entityType, [FromQuery] Guid? entityId,
        [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsAdmin)
        {
            if (entityType is not AuditEntityType.Consultation) return Forbid();
        }
        take = Math.Clamp(take, 1, 500);

        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (entityType is not null) q = q.Where(a => a.EntityType == entityType);
        if (entityId is not null) q = q.Where(a => a.EntityId == entityId);
        return Ok(await q.OrderByDescending(a => a.PerformedAt).Take(take).ToListAsync(ct));
    }

    /// <summary>§16.2 — paged + searched audit trail for the admin screen.
    /// `q` matches action / details.</summary>
    [Authorize(Roles = "Admin")]
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

    /// <summary>Client-invented audit rows are no longer accepted. Controllers write
    /// audit via <c>AuditLogWriter</c> on real mutations. Kept as 410 so old FE callers fail closed.</summary>
    [Authorize(Roles = "Admin,Doctor,Staff")]
    [HttpPost]
    public IActionResult Create(CreateAuditLogRequest req) =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            message = "Audit rows are written by the server on real actions; client POST is disabled."
        });
}
