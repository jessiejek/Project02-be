//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.1: sync review items. The admin lists and resolves them on the web; a desktop uploads the ones only it knows about (a change the server refused, or one stuck after repeated tries).
using System.Security.Claims;
using System.Text.Json;
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Sync;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync/review-items")]
public class ReviewItemsController(ClinicAppDbContext db) : ControllerBase
{
    public const int PageSizeMax = 100;
    public const int MaxUploadBatch = 100;

    private static object Row(SyncReviewItem i) => new
    {
        id = i.Id, kind = i.Kind, status = i.Status, title = i.Title,
        details = string.IsNullOrWhiteSpace(i.DetailsJson) ? (JsonElement?)null : JsonDocument.Parse(i.DetailsJson).RootElement.Clone(),
        patient_id = i.PatientId, other_patient_id = i.OtherPatientId,
        aggregate = i.Aggregate, entity_id = i.EntityId, other_entity_id = i.OtherEntityId, origin_node_id = i.OriginNodeId,
        created_at = i.CreatedAt, resolved_at = i.ResolvedAt, resolved_by_user_id = i.ResolvedByUserId,
        resolution = i.Resolution, resolution_note = i.ResolutionNote,
    };

    // ── admin ────────────────────────────────────────────────────────────
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? kind, [FromQuery] int page = 1, [FromQuery(Name = "page_size")] int pageSize = 25, CancellationToken ct = default)
    {
        if (status is not null && status is not (ReviewItemStatus.Open or ReviewItemStatus.Resolved)) return BadRequest(new { message = "status must be open or resolved." });
        if (kind is not null && !ReviewItemKinds.All.Contains(kind)) return BadRequest(new { message = "Unknown kind." });
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, PageSizeMax);
        var q = db.SyncReviewItems.AsNoTracking().AsQueryable();
        if (status is not null) q = q.Where(i => i.Status == status);
        if (kind is not null) q = q.Where(i => i.Kind == kind);
        var total = await q.CountAsync(ct);
        var openTotal = await db.SyncReviewItems.AsNoTracking().CountAsync(i => i.Status == ReviewItemStatus.Open, ct);
        var items = await q.OrderByDescending(i => i.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { items = items.Select(Row), total, open_total = openTotal, page, page_size = pageSize });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await db.SyncReviewItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
        return item is null ? NotFound() : Ok(Row(item));
    }

    public sealed record ResolveRequest(string? Resolution, string? Note);

    /// <summary>Close an item with a note. 'merged' is only set by the merge endpoint (step 4.4).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, ResolveRequest request, CancellationToken ct)
    {
        var item = await db.SyncReviewItems.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();
        var resolution = request.Resolution?.Trim().ToLowerInvariant();
        if (resolution is not (ReviewResolutions.Dismissed or ReviewResolutions.Fixed))
            return BadRequest(new { message = "Resolution must be 'dismissed' or 'fixed'. A duplicate is closed by merging." });
        var note = request.Note?.Trim() ?? "";
        if (note.Length < 5) return BadRequest(new { message = "Write a note of at least 5 characters." });
        if (note.Length > 1000) return BadRequest(new { message = "The note is too long (1000 characters at most)." });
        if (item.Status == ReviewItemStatus.Resolved) return Conflict(new { error = "already_resolved", message = "This item is already resolved." });

        item.Status = ReviewItemStatus.Resolved;
        item.Resolution = resolution;
        item.ResolutionNote = note;
        item.ResolvedAt = DateTimeOffset.UtcNow;
        item.ResolvedByUserId = CurrentUserId();
        AuditLogWriter.Add(db, AuditEntityType.Settings, item.Id, "Sync review item resolved", item.ResolvedByUserId, $"{item.Kind}: {resolution}");
        await db.SaveChangesAsync(ct);
        return Ok(Row(item));
    }

    // ── desktops ─────────────────────────────────────────────────────────
    public sealed record UploadedItem(Guid Id, string? Kind, string? Title, JsonElement? Details, string? Aggregate, string? EntityId, string? OtherEntityId, DateTimeOffset? CreatedAt);
    public sealed record UploadRequest(List<UploadedItem>? Items);

    /// <summary>A desktop sends the problems only it knows about. Idempotent: an id already stored is skipped, and the same problem is not listed twice.</summary>
    [Authorize(Roles = SyncNodeAuth.Role)]
    [HttpPost]
    public async Task<IActionResult> Upload(UploadRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(SyncNodeAuth.NodeIdClaim)?.Value, out var nodeId)) return Unauthorized();
        var items = request.Items ?? [];
        if (items.Count == 0) return BadRequest(new { error = "empty_batch" });
        if (items.Count > MaxUploadBatch) return BadRequest(new { error = "batch_too_large", detail = $"At most {MaxUploadBatch} items per call." });

        var accepted = new List<Guid>(); var skipped = new List<Guid>(); var invalid = new List<object>();
        foreach (var u in items)
        {
            var kind = u.Kind?.Trim();
            var title = u.Title?.Trim() ?? "";
            if (u.Id == Guid.Empty || kind is null || !ReviewItemKinds.FromNodes.Contains(kind) || title.Length == 0 || title.Length > 300
                || (u.EntityId?.Length ?? 0) > 100 || (u.OtherEntityId?.Length ?? 0) > 100 || (u.Aggregate?.Length ?? 0) > 64)
            {
                invalid.Add(new { id = u.Id, reason = "invalid_item" });
                continue;
            }
            var entityId = string.IsNullOrWhiteSpace(u.EntityId) ? null : u.EntityId.Trim();
            var other = string.IsNullOrWhiteSpace(u.OtherEntityId) ? null : u.OtherEntityId.Trim();
            var exists = await db.SyncReviewItems.AnyAsync(i => i.Id == u.Id || (i.Kind == kind && i.EntityId == entityId && i.OtherEntityId == other), ct);
            if (exists) { skipped.Add(u.Id); continue; }
            db.SyncReviewItems.Add(new SyncReviewItem
            {
                Id = u.Id, Kind = kind, Status = ReviewItemStatus.Open, Title = title,
                DetailsJson = u.Details is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } d ? d.GetRawText() : null,
                Aggregate = u.Aggregate?.Trim(), EntityId = entityId, OtherEntityId = other, OriginNodeId = nodeId,
                CreatedAt = u.CreatedAt ?? DateTimeOffset.UtcNow,
            });
            AuditLogWriter.Add(db, AuditEntityType.Settings, u.Id, "Sync review item received from a computer", null, $"{kind} from node {nodeId}");
            accepted.Add(u.Id);
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { accepted, skipped, invalid });
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
//JJQR - END
