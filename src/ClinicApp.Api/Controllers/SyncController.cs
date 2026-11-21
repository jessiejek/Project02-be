//JJQR - AI (Nov 21, 2026) cloud sync endpoints (contract: Project02-desktop/docs/SYNC-API-CONTRACT.md)
using System.Security.Claims;
using ClinicApp.Api.Sync;
using ClinicApp.Domain; //JJQR - AI (21 Nov 2026) // JJQR - END
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums; //JJQR - AI (21 Nov 2026) // JJQR - END
using ClinicApp.Infrastructure;
using ClinicApp.Infrastructure.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncController(ClinicAppDbContext db, SyncNodeAuth nodeAuth, IPatientCodeAllocator codes, IConfiguration config) : ControllerBase
{
    public const int ProtocolVersion = 1;

    // ── node registry (cloud admin) ─────────────────────────────────────
    public sealed record RegisterNodeRequest(Guid? NodeId, string? Name, string? Platform, string? AppVersion);

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes")]
    public async Task<IActionResult> Register(RegisterNodeRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { error = "invalid_request", detail = "name is required." });
        var nodeId = request.NodeId is Guid g && g != Guid.Empty ? g : Guid.NewGuid();
        if (await db.SyncNodes.AnyAsync(n => n.NodeId == nodeId, ct)) return Conflict(new { error = "node_exists" });
        var secret = SyncNodeAuth.NewSecret();
        var node = new SyncNode
        {
            NodeId = nodeId, Name = name, Platform = request.Platform, AppVersion = request.AppVersion,
            SecretHash = SyncNodeAuth.Hash(secret), Status = SyncNodeStatus.Active,
            RegisteredByUserId = UserId(), RegisteredAt = DateTimeOffset.UtcNow,
        };
        db.SyncNodes.Add(node);
        await db.SaveChangesAsync(ct);
        return StatusCode(201, new { node_id = node.NodeId, name = node.Name, node_secret = secret, status = node.Status, registered_at = node.RegisteredAt });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("nodes")]
    public async Task<IActionResult> ListNodes(CancellationToken ct)
    {
        //JJQR - AI (21 Nov 2026) per-node activity: last push time + 24h operation / rejected counts (no secret material)
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var ops = await db.SyncOperations.AsNoTracking()
            .GroupBy(o => o.NodeId)
            .Select(g => new
            {
                NodeId = g.Key,
                LastPushAt = g.Max(o => (DateTimeOffset?)o.ReceivedAt),
                Count24h = g.Count(o => o.ReceivedAt >= since),
                Rejected24h = g.Count(o => o.ReceivedAt >= since && o.Status == SyncStatuses.Rejected),
            }).ToListAsync(ct);
        var byNode = ops.ToDictionary(o => o.NodeId);
        return Ok((await db.SyncNodes.AsNoTracking().ToListAsync(ct)).OrderBy(n => n.RegisteredAt).Select(n =>
        {
            byNode.TryGetValue(n.NodeId, out var o);
            return new
            {
                node_id = n.NodeId, name = n.Name, platform = n.Platform, app_version = n.AppVersion, status = n.Status,
                registered_at = n.RegisteredAt, last_seen_at = n.LastSeenAt, revoked_at = n.RevokedAt,
                last_push_at = o?.LastPushAt, operation_count_24h = o?.Count24h ?? 0, rejected_count_24h = o?.Rejected24h ?? 0
            };
        }));
        //JJQR - END
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{nodeId:guid}/rotate-secret")]
    public async Task<IActionResult> Rotate(Guid nodeId, CancellationToken ct)
    {
        var node = await db.SyncNodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node is null) return NotFound(new { error = "node_not_found" });
        var secret = SyncNodeAuth.NewSecret();
        node.SecretHash = SyncNodeAuth.Hash(secret);
        await db.SaveChangesAsync(ct);
        return Ok(new { node_id = node.NodeId, node_secret = secret });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("nodes/{nodeId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid nodeId, CancellationToken ct)
    {
        var node = await db.SyncNodes.FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
        if (node is null) return NotFound(new { error = "node_not_found" });
        if (node.Status != SyncNodeStatus.Revoked)
        {
            node.Status = SyncNodeStatus.Revoked;
            node.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { node_id = node.NodeId, status = node.Status, revoked_at = node.RevokedAt });
    }

    // ── node token ──────────────────────────────────────────────────────
    public sealed record TokenRequest(Guid NodeId, string? NodeSecret);

    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<IActionResult> Token(TokenRequest request, CancellationToken ct)
    {
        var node = await db.SyncNodes.FirstOrDefaultAsync(n => n.NodeId == request.NodeId, ct);
        if (node is null || string.IsNullOrEmpty(request.NodeSecret) || !SyncNodeAuth.Verify(request.NodeSecret, node.SecretHash))
            return Unauthorized(new { error = "invalid_node_credentials" });
        if (node.Status != SyncNodeStatus.Active) return StatusCode(403, new { error = "node_revoked" });
        node.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var (token, expires) = nodeAuth.CreateToken(node.NodeId);
        return Ok(new { access_token = token, token_type = "Bearer", expires_in = SyncNodeAuth.TokenMinutes * 60, expires_at = expires, node_id = node.NodeId });
    }

    // ── handshake / push / pull (node token) ────────────────────────────
    [Authorize(Roles = SyncNodeAuth.Role)]
    [HttpGet("handshake")]
    public async Task<IActionResult> Handshake(CancellationToken ct)
    {
        if (ProtocolError() is { } bad) return bad;
        var node = await TouchNodeAsync(ct);
        return Ok(new { protocol_version = ProtocolVersion, min_protocol_version = 1, node_id = node.NodeId, server_time = DateTimeOffset.UtcNow, node_status = node.Status });
    }

    [Authorize(Roles = SyncNodeAuth.Role)]
    [HttpPost("push")]
    public async Task<IActionResult> Push(SyncPushRequest request, CancellationToken ct)
    {
        if (ProtocolError() is { } bad) return bad;
        var ops = request.Operations ?? [];
        if (ops.Count == 0) return BadRequest(new { error = "empty_batch" });
        if (ops.Count > SyncPushEngine.MaxBatch) return BadRequest(new { error = "batch_too_large", detail = $"At most {SyncPushEngine.MaxBatch} operations per call." });
        if (ops.Any(o => o.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(o.Aggregate)))
            return BadRequest(new { error = "invalid_operation", detail = "Every operation needs operation_id and aggregate." });
        var node = await TouchNodeAsync(ct);
        var results = await new SyncPushEngine(db, codes).PushAsync(node.NodeId, ops, ct);
        return Ok(new SyncPushResponse { ServerTime = DateTimeOffset.UtcNow, Results = results });
    }

    [Authorize(Roles = SyncNodeAuth.Role)]
    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] string? since, [FromQuery] int limit = SyncPullService.DefaultLimit,
        [FromQuery(Name = "include_own")] bool includeOwn = false, CancellationToken ct = default)
    {
        if (ProtocolError() is { } bad) return bad;
        if (!SyncPullService.TryParseCursor(since, out var seq)) return BadRequest(new { error = "invalid_cursor" });
        if (limit < 1 || limit > SyncPullService.MaxLimit) return BadRequest(new { error = "invalid_limit", detail = $"limit must be 1–{SyncPullService.MaxLimit}." });
        var node = await TouchNodeAsync(ct);
        var settle = TimeSpan.FromSeconds(config.GetValue("Sync:PullSettleSeconds", 2.0));
        return Ok(await new SyncPullService(db).PullAsync(node.NodeId, seq, limit, includeOwn, settle, ct));
    }

    //JJQR - AI (21 Nov 2026) offline queue numbers: a node reserves a block of numbers while online and issues only from it while offline
    public sealed record QueueBlockRequest(DateOnly? Date, string? Lane, int? Size);

    [Authorize(Roles = SyncNodeAuth.Role)]
    [HttpPost("queue-blocks")]
    public async Task<IActionResult> QueueBlock(QueueBlockRequest request, CancellationToken ct)
    {
        if (ProtocolError() is { } bad) return bad;
        var lane = request.Lane?.Trim().ToUpperInvariant();
        if (lane is not ("R" or "S")) return BadRequest(new { error = "invalid_request", detail = "lane must be R or S." });
        if (request.Date is not { } day) return BadRequest(new { error = "invalid_request", detail = "date is required (yyyy-MM-dd)." });
        var size = Math.Clamp(request.Size ?? 20, 5, 50);

        var node = await TouchNodeAsync(ct);
        if (node.Status != SyncNodeStatus.Active) return StatusCode(403, new { error = "node_revoked" });
        var (from, to) = await QueueSequencer.ReserveBlockAsync(db, day, lane, size, ct);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), EntityType = AuditEntityType.Settings, EntityId = node.NodeId, Action = "Queue block reserved",
            PerformedAt = DateTimeOffset.UtcNow, Details = $"Node {node.NodeId}: {day:yyyy-MM-dd} lane {lane} {QueueLane.Format(lane, from)}..{QueueLane.Format(lane, to)} ({size})"
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { date = day.ToString("yyyy-MM-dd"), lane, from, to, size, first = QueueLane.Format(lane, from), last = QueueLane.Format(lane, to) });
    }
    //JJQR - END

    private IActionResult? ProtocolError()
    {
        var raw = Request.Headers["X-Sync-Protocol"].ToString();
        if (string.IsNullOrEmpty(raw) || raw == "1") return null;
        return StatusCode(426, new { error = "protocol_unsupported", supported = new[] { ProtocolVersion } });
    }

    private async Task<SyncNode> TouchNodeAsync(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(SyncNodeAuth.NodeIdClaim)!);
        var node = await db.SyncNodes.FirstAsync(n => n.NodeId == id, ct);
        node.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return node;
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
//JJQR - END
