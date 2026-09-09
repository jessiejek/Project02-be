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
