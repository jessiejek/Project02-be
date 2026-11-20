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
        // JJQR - AI (Nov 20, 2026) - PV-5: the change log does not fill up with "who looked" / sign-in rows unless they are asked for by type
        else q = q.Where(a => a.EntityType != AuditEntityType.Access && a.EntityType != AuditEntityType.Auth);
        // JJQR - END
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
        // JJQR - AI (Nov 20, 2026) - PV-5: same default as above
        else query = query.Where(a => a.EntityType != AuditEntityType.Access && a.EntityType != AuditEntityType.Auth);
        // JJQR - END
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            // JJQR - AI (Nov 20, 2026) - PV-9: names are no longer written into the log, so the admin can also search by the record's id
            if (Guid.TryParse(s, out var rid)) query = query.Where(a => a.EntityId == rid || a.Action.Contains(s) || (a.Details != null && a.Details.Contains(s)));
            else
            // JJQR - END
            query = query.Where(a => a.Action.Contains(s) || (a.Details != null && a.Details.Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.PerformedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResult<AuditLog> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    // JJQR - AI (Nov 20, 2026) - PV-5: admin view "who looked at this patient"
    public record AccessEntry(DateTimeOffset At, string Who, string Role, string Area);

    [Authorize(Roles = "Admin")]
    [HttpGet("patient/{patientId:guid}/access")]
    public async Task<ActionResult<List<AccessEntry>>> PatientAccess(Guid patientId, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == AuditEntityType.Access && a.EntityId == patientId)
            .OrderByDescending(a => a.PerformedAt).Take(take).ToListAsync(ct);
        var ids = rows.Select(r => r.PerformedByUserId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var staff = await db.StaffAccounts.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        return Ok(rows.Select(r =>
        {
            var s = r.PerformedByUserId is { } u && staff.TryGetValue(u, out var st) ? st : null;
            return new AccessEntry(r.PerformedAt, s?.FullName ?? "Unknown user", s?.Role.ToString() ?? "", r.Details ?? "");
        }).ToList());
    }
    // JJQR - END

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
