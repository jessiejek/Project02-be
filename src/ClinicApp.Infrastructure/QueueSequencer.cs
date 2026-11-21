using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

/// <summary>Single source of the per-day `queue_number` sequences (§16.3), shared by
/// every path that creates a booking (staff walk-in check-in, online self-booking, admin
/// booking creation, reschedule) so they can never mint duplicate numbers for the same day. Backed by
/// `QueueCounter` (one row per calendar day PER LANE) and incremented with optimistic-concurrency
/// retry, which is portable across both the SQLite dev provider and SQL Server prod —
/// unlike a raw-SQL lock hint, which would only work on one of them.</summary>
public static class QueueSequencer
{
    //JJQR - AI (Nov 21, 2026) - priority lane: every caller names the patient so the lane (R or S) is decided in ONE place
    /// <summary>Next queue number for <paramref name="patientId"/> on <paramref name="day"/>:
    /// "S-003" for a senior / PWD (see <see cref="QueueLane.For"/>), otherwise "R-014".</summary>
    public static async Task<string> NextForPatientAsync(
        ClinicAppDbContext db, DateOnly day, Guid patientId, string? discountCategory, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == patientId, ct);
        return await NextAsync(db, day, QueueLane.For(patient, discountCategory, day), ct);
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) reserve a block of <paramref name="size"/> numbers for an offline node: raises the SAME per-day counter by size in one atomic step,
    /// so online check-ins can never be issued a number inside the block. Returns the first and last sequence number of the block.
    public static async Task<(int From, int To)> ReserveBlockAsync(ClinicAppDbContext db, DateOnly day, string lane, int size, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (attempt > 0) await Task.Delay(Random.Shared.Next(5, 40), ct);
            var counter = await db.QueueCounters.AsNoTracking().SingleOrDefaultAsync(c => c.Date == day && c.Lane == lane, ct);
            if (counter is null)
            {
                db.QueueCounters.Add(new QueueCounter { Date = day, Lane = lane, Value = size });
                try
                {
                    await db.SaveChangesAsync(ct);
                    return (1, size);
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                    continue;
                }
            }
            var next = counter.Value + size;
            var affected = await db.QueueCounters
                .Where(c => c.Date == day && c.Lane == lane && c.Value == counter.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Value, next), ct);
            if (affected == 1) return (counter.Value + 1, next);
        }
        throw new InvalidOperationException("Could not reserve a queue block after multiple attempts.");
    }
    //JJQR - END

    public static async Task<string> NextAsync(ClinicAppDbContext db, DateOnly day, string lane, CancellationToken ct)
    {
        // JJQR - AI (Nov 20, 2026) - F-1: read the counter WITHOUT tracking. A tracked entity keeps its first-read Value, so every retry after a
        // collision re-used the stale number and failed again ("Could not allocate a queue number" 500 under simultaneous check-ins). Retries now also wait a few ms.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            if (attempt > 0) await Task.Delay(Random.Shared.Next(5, 40), ct);
            var counter = await db.QueueCounters.AsNoTracking().SingleOrDefaultAsync(c => c.Date == day && c.Lane == lane, ct);
            // JJQR - END
            if (counter is null)
            {
                db.QueueCounters.Add(new QueueCounter { Date = day, Lane = lane, Value = 1 });
                try
                {
                    await db.SaveChangesAsync(ct);
                    return QueueLane.Format(lane, 1);
                }
                catch (DbUpdateException)
                {
                    // Another request inserted today's counter row first — retry and update it instead.
                    db.ChangeTracker.Clear();
                    continue;
                }
            }

            var next = counter.Value + 1;
            var affected = await db.QueueCounters
                .Where(c => c.Date == day && c.Lane == lane && c.Value == counter.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Value, next), ct);
            if (affected == 1) return QueueLane.Format(lane, next);
            // Someone else incremented it between our read and write — retry with the new value.
        }

        throw new InvalidOperationException("Could not allocate a queue number after multiple attempts.");
    }
}
