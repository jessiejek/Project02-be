//JJQR - AI (Nov 21, 2026) cloud sync: SQLite schema upgrade for existing files + change-log backfill
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure.Sync;

public static class SyncBootstrap
{
    /// <summary>SQLite only: EnsureCreated() never alters an existing file, so a dev / node
    /// database created before cloud sync lacks the sync tables and columns. Adds them in place.</summary>
    public static void EnsureSqliteSyncSchema(ClinicAppDbContext db)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite") return;
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "sync_nodes" (
                "node_id" TEXT NOT NULL CONSTRAINT "pk_sync_nodes" PRIMARY KEY,
                "name" TEXT NOT NULL, "platform" TEXT NULL, "app_version" TEXT NULL,
                "secret_hash" TEXT NOT NULL, "status" TEXT NOT NULL, "registered_by_user_id" TEXT NULL,
                "registered_at" INTEGER NOT NULL, "last_seen_at" INTEGER NULL, "revoked_at" INTEGER NULL);
            """);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "sync_operations" (
                "operation_id" TEXT NOT NULL CONSTRAINT "pk_sync_operations" PRIMARY KEY,
                "node_id" TEXT NOT NULL, "aggregate" TEXT NOT NULL, "entity_id" TEXT NOT NULL,
                "operation_type" TEXT NOT NULL, "status" TEXT NOT NULL, "reason_code" TEXT NULL,
                "result_json" TEXT NOT NULL, "node_created_at" INTEGER NULL, "received_at" INTEGER NOT NULL);
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "ix_sync_operations_node_id_received_at" ON "sync_operations" ("node_id", "received_at");""");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "sync_changes" (
                "seq" INTEGER NOT NULL CONSTRAINT "pk_sync_changes" PRIMARY KEY AUTOINCREMENT,
                "aggregate" TEXT NOT NULL, "entity_id" TEXT NOT NULL, "deleted" INTEGER NOT NULL,
                "row_version" INTEGER NOT NULL, "origin_node_id" TEXT NULL, "changed_at" INTEGER NOT NULL);
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "ix_sync_changes_aggregate_entity_id" ON "sync_changes" ("aggregate", "entity_id");""");

        //JJQR - AI (Nov 21, 2026) offline self-edits: per-field last-write times
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "sync_field_stamps" (
                "aggregate" TEXT NOT NULL, "entity_id" TEXT NOT NULL, "field" TEXT NOT NULL,
                "stamped_at" INTEGER NOT NULL, "origin_node_id" TEXT NULL,
                CONSTRAINT "pk_sync_field_stamps" PRIMARY KEY ("aggregate", "entity_id", "field"));
            """);
        //JJQR - END

        foreach (var et in db.Model.GetEntityTypes().Where(t => typeof(ISyncTracked).IsAssignableFrom(t.ClrType)))
        {
            var table = et.GetTableName();
            if (table is null) continue;
            var cols = db.Database.SqlQueryRaw<string>($"SELECT name AS \"Value\" FROM pragma_table_info('{table}')").ToList();
            if (cols.Count == 0) continue;
            if (!cols.Contains("row_version")) db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"row_version\" INTEGER NOT NULL DEFAULT 0;");
            if (!cols.Contains("deleted_at")) db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"deleted_at\" INTEGER NULL;");
            if (!cols.Contains("origin_node_id")) db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"origin_node_id\" TEXT NULL;");
        }
    }

    /// <summary>First run with sync: seed the change log with every existing synced row so a
    /// node's first pull (since=0) receives the full data set. Adds a change row only for entities that have none yet,
    /// so it is a cheap no-op once everything is logged.</summary>
    public static async Task BackfillAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        //JJQR - AI (Nov 21, 2026) per ENTITY, not per aggregate: rows inserted without going through SaveChanges (seed data, HasData, restored files) are logged too
        var now = DateTimeOffset.UtcNow;
        var rows = new List<SyncChange>();
        async Task AddIdsAsync(string aggregate, IEnumerable<string> ids)
        {
            var have = (await db.SyncChanges.AsNoTracking().Where(c => c.Aggregate == aggregate).Select(c => c.EntityId).Distinct().ToListAsync(ct)).ToHashSet();
            rows.AddRange(ids.Where(id => !have.Contains(id)).Select(id => new SyncChange { Aggregate = aggregate, EntityId = id, ChangedAt = now }));
        }
        //JJQR - END

        await AddIdsAsync(SyncAggregates.Patient, (await db.Patients.AsNoTracking().Select(x => x.PatientId).ToListAsync(ct)).Select(SyncAggregates.Id));
        await AddIdsAsync(SyncAggregates.Booking, (await db.Bookings.AsNoTracking().Select(x => x.BookingId).ToListAsync(ct)).Select(SyncAggregates.Id));
        await AddIdsAsync(SyncAggregates.Payment, (await db.Payments.AsNoTracking().Select(x => x.PaymentId).ToListAsync(ct)).Select(SyncAggregates.Id));
        await AddIdsAsync(SyncAggregates.Consultation, (await db.Consultations.AsNoTracking().Select(x => x.ConsultationId).ToListAsync(ct)).Select(SyncAggregates.Id));
        await AddIdsAsync(SyncAggregates.VitalReading, (await db.PatientVitalReadings.AsNoTracking().Select(x => x.Id).ToListAsync(ct)).Select(SyncAggregates.Id));
        foreach (var (name, clr) in SyncAggregates.PullOnly)
        {
            var et = db.Model.FindEntityType(clr)!;
            var all = await QueryAll(db, clr).ToListAsync(ct);
            await AddIdsAsync(name, all.Select(e => SyncAggregates.KeyString(et, e)));
        }
        //JJQR - AI (21 Nov 2026) audit logs: only the last 30 days are seeded so a new node's first pull gets recent history (the cloud keeps everything)
        await AddIdsAsync(SyncAggregates.AuditLog, (await db.AuditLogs.AsNoTracking()
            .Where(a => a.PerformedAt >= now.AddDays(-SyncPullService.AuditPullDays)).Select(a => a.Id).ToListAsync(ct)).Select(SyncAggregates.Id));
        //JJQR - END
        // Audit logs older than that are not backfilled (history stays in the cloud); new ones flow from now on.
        if (rows.Count == 0) return;
        db.SyncChangeLogEnabled = false;
        try
        {
            db.SyncChanges.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.SyncChangeLogEnabled = true;
            db.ChangeTracker.Clear();
        }
    }

    private static IQueryable<object> QueryAll(ClinicAppDbContext db, Type clr)
    {
        var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(clr).Invoke(db, null)!;
        var noTracking = typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!
            .MakeGenericMethod(clr).Invoke(null, [set])!;
        return ((IQueryable)noTracking).Cast<object>();
    }
}
//JJQR - END
