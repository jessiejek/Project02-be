//JJQR - AI (Nov 21, 2026) cloud sync: bump row_version + write sync_changes rows inside SaveChanges
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums; //JJQR - AI (Nov 21, 2026) offline self-edits // JJQR - END
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure.Sync;

internal static class SyncChangeRecorder
{
    public static void Record(ClinicAppDbContext db)
    {
        var entries = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not (SyncChange or SyncOperation or SyncNode or SyncFieldStamp))
            .ToList();
        if (entries.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var writer = db.SyncWriterNodeId;
        var changes = new Dictionary<(string, string), SyncChange>();
        var bumpedRoots = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entry in entries)
        {
            var target = SyncAggregates.Resolve(db, entry);
            if (target is null) continue;
            var t = target.Value;

            if (entry.Entity is ISyncTracked tracked && entry.State != EntityState.Deleted)
            {
                tracked.RowVersion += 1;
                if (entry.State == EntityState.Added && writer is not null && tracked.OriginNodeId is null)
                    tracked.OriginNodeId = writer;
                bumpedRoots.Add(entry.Entity);
            }

            var deleted = t.IsRoot && (entry.State == EntityState.Deleted
                || (entry.Entity is ISyncTracked st && st.DeletedAt is not null));
            var rowVersion = entry.Entity is ISyncTracked rv && t.IsRoot ? rv.RowVersion : 0;

            if (!t.IsRoot) BumpTrackedRoot(db, t, bumpedRoots, ref rowVersion);

            if (changes.TryGetValue((t.Aggregate, t.EntityId), out var existing))
            {
                existing.Deleted |= deleted;
                existing.RowVersion = Math.Max(existing.RowVersion, rowVersion);
            }
            else
            {
                changes[(t.Aggregate, t.EntityId)] = new SyncChange
                {
                    Aggregate = t.Aggregate,
                    EntityId = t.EntityId,
                    Deleted = deleted,
                    RowVersion = rowVersion,
                    OriginNodeId = writer,
                    ChangedAt = now,
                };
            }
        }
        if (changes.Count > 0) db.SyncChanges.AddRange(changes.Values);
        StampFields(db, entries, now, writer); //JJQR - AI (Nov 21, 2026) offline self-edits // JJQR - END
    }

    //JJQR - AI (Nov 21, 2026) offline self-edits: remember when each field of a doctor / schedule day / blocked date was last written,
    // so a node's older offline edit can lose (and a newer one win) per field. Web edits are stamped with the cloud clock; the push
    // engine passes the node's clock for the fields it applies through db.SyncFieldTimes.
    private static readonly HashSet<string> NeverStamped = new() { "created_at", "updated_at", "doctor_id", "day_of_week", "id", "blocked_date", "staff_id", "user_id" };

    private static void StampFields(ClinicAppDbContext db, List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> entries, DateTimeOffset now, Guid? writer)
    {
        var done = new HashSet<(string, string, string)>();
        foreach (var entry in entries.Where(e => e.State == EntityState.Modified))
        {
            string aggregate, entityId, prefix = "";
            switch (entry.Entity)
            {
                case Doctor d: (aggregate, entityId) = (SyncAggregates.Doctor, SyncAggregates.Id(d.DoctorId)); break;
                case StaffAccount s when s.Role == StaffRole.Doctor: (aggregate, entityId, prefix) = (SyncAggregates.Doctor, SyncAggregates.Id(s.StaffId), "staff."); break;
                case DoctorSchedule sc: (aggregate, entityId) = (SyncAggregates.DoctorSchedule, SyncAggregates.Id(sc.Id)); break;
                case DoctorBlockedDate b: (aggregate, entityId) = (SyncAggregates.DoctorBlockedDate, SyncAggregates.Id(b.Id)); break;
                default: continue;
            }
            foreach (var prop in entry.Properties.Where(p => p.IsModified))
            {
                var name = SyncRows.Name(prop.Metadata);
                if (NeverStamped.Contains(name)) continue;
                if (prefix.Length > 0 && name is not ("full_name" or "contact_number")) continue;
                var field = prefix + name;
                var at = db.SyncFieldTimes.TryGetValue((aggregate, entityId, field), out var t) ? t : now;
                Upsert(db, aggregate, entityId, field, at, writer);
                done.Add((aggregate, entityId, field));
            }
        }
        // fields the push engine registered for a row it just created (state Added): stamp them with the node's time
        foreach (var (key, at) in db.SyncFieldTimes)
            if (!done.Contains(key)) Upsert(db, key.Aggregate, key.EntityId, key.Field, at, writer);
        db.SyncFieldTimes.Clear();
    }

    private static void Upsert(ClinicAppDbContext db, string aggregate, string entityId, string field, DateTimeOffset at, Guid? writer)
    {
        var stamp = db.SyncFieldStamps.Find(aggregate, entityId, field);
        if (stamp is null)
            db.SyncFieldStamps.Add(new SyncFieldStamp { Aggregate = aggregate, EntityId = entityId, Field = field, StampedAt = at, OriginNodeId = writer });
        else { stamp.StampedAt = at; stamp.OriginNodeId = writer; }
    }
    //JJQR - END

    /// <summary>A child row changed: bump the root's row_version too when the root is loaded.</summary>
    private static void BumpTrackedRoot(ClinicAppDbContext db, SyncAggregates.Target t, HashSet<object> bumped, ref long rowVersion)
    {
        if (!Guid.TryParse(t.EntityId, out var id)) return;
        ISyncTracked? root = t.Aggregate switch
        {
            SyncAggregates.Patient => db.Patients.Local.FirstOrDefault(p => p.PatientId == id), //JJQR - AI (Nov 21, 2026) allergies // JJQR - END
            SyncAggregates.Booking => db.Bookings.Local.FirstOrDefault(b => b.BookingId == id),
            SyncAggregates.Consultation => db.Consultations.Local.FirstOrDefault(c => c.ConsultationId == id),
            _ => null
        };
        if (root is null) return;
        if (!bumped.Contains(root))
        {
            var entry = db.Entry(root);
            if (entry.State == EntityState.Deleted) return;
            root.RowVersion += 1;
            bumped.Add(root);
        }
        rowVersion = Math.Max(rowVersion, root.RowVersion);
    }
}
//JJQR - END
