//JJQR - AI (Nov 21, 2026) cloud sync: paged pull from the change log
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure.Sync;

public sealed class SyncPullService(ClinicAppDbContext db)
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 1000;
    //JJQR - AI (21 Nov 2026) a node only receives audit rows from the last 30 days (the cloud keeps the full history)
    public const int AuditPullDays = 30;
    //JJQR - END

    public static bool TryParseCursor(string? since, out long seq)
    {
        seq = 0;
        if (string.IsNullOrWhiteSpace(since)) return true;
        return long.TryParse(since, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seq);
    }

    /// <param name="settle">Changes younger than this are held back one cycle, so a slower
    /// transaction holding a lower sequence number is not skipped.</param>
    public async Task<SyncPullResponse> PullAsync(Guid nodeId, long since, int limit, bool includeOwn, TimeSpan settle, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var now = DateTimeOffset.UtcNow;
        var page = await db.SyncChanges.AsNoTracking()
            .Where(c => c.Seq > since)
            .OrderBy(c => c.Seq)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = page.Count > limit;
        if (hasMore) page.RemoveAt(page.Count - 1);
        if (settle > TimeSpan.Zero)
        {
            var cutoff = now - settle;
            var firstYoung = page.FindIndex(c => c.ChangedAt > cutoff);
            if (firstYoung >= 0)
            {
                page.RemoveRange(firstYoung, page.Count - firstYoung);
                hasMore = true;
            }
        }

        //JJQR - AI (21 Nov 2026) drop audit_log changes whose audit row is older than the window (rows already deleted stay, they only produce a delete)
        var cursorSeq = page.Count > 0 ? page[^1].Seq : since; // taken BEFORE filtering so the cursor still moves past skipped rows
        var auditIds = page.Where(c => c.Aggregate == SyncAggregates.AuditLog && Guid.TryParse(c.EntityId, out _))
            .Select(c => Guid.Parse(c.EntityId)).Distinct().ToList();
        if (auditIds.Count > 0)
        {
            var cutoffAudit = now.AddDays(-AuditPullDays);
            var tooOld = (await db.AuditLogs.AsNoTracking().Where(a => auditIds.Contains(a.Id) && a.PerformedAt < cutoffAudit).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
            if (tooOld.Count > 0)
                page.RemoveAll(c => c.Aggregate == SyncAggregates.AuditLog && Guid.TryParse(c.EntityId, out var aid) && tooOld.Contains(aid));
        }
        //JJQR - END

        var response = new SyncPullResponse
        {
            ServerTime = now,
            NextCursor = cursorSeq.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HasMore = hasMore,
        };

        // One entry per aggregate per page, at its latest change.
        var latest = page.GroupBy(c => (c.Aggregate, c.EntityId)).Select(g => g.OrderBy(c => c.Seq).Last()).OrderBy(c => c.Seq);
        foreach (var change in latest)
        {
            if (!includeOwn && change.OriginNodeId == nodeId) continue;
            var doc = await SyncDocuments.BuildAsync(db, change.Aggregate, change.EntityId, ct);
            if (ReferenceEquals(doc, SyncDocuments.Hidden)) continue;
            response.Changes.Add(new SyncPullChange
            {
                Aggregate = change.Aggregate,
                EntityId = change.EntityId,
                Operation = doc is null ? "delete" : "upsert",
                RowVersion = doc is null || doc.RowVersion <= 0 ? change.Seq : doc.RowVersion,
                OriginNodeId = change.OriginNodeId,
                ChangedAt = change.ChangedAt,
                Data = doc?.Data,
            });
        }
        db.ChangeTracker.Clear();
        return response;
    }
}
//JJQR - END
