using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

/// <summary>Single source of the per-day FCFS `queue_number` sequence (§16.3), shared by
/// every path that creates a booking (staff walk-in check-in, online self-booking, admin
/// booking creation) so they can never mint duplicate numbers for the same day. Backed by
/// `QueueCounter` (one row per calendar day) and incremented with optimistic-concurrency
/// retry, which is portable across both the SQLite dev provider and SQL Server prod —
/// unlike a raw-SQL lock hint, which would only work on one of them.</summary>
public static class QueueSequencer
{
    public static async Task<string> NextAsync(ClinicAppDbContext db, DateOnly day, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var counter = await db.QueueCounters.SingleOrDefaultAsync(c => c.Date == day, ct);
            if (counter is null)
            {
                db.QueueCounters.Add(new QueueCounter { Date = day, Value = 1 });
                try
                {
                    await db.SaveChangesAsync(ct);
                    return Format(1);
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
                .Where(c => c.Date == day && c.Value == counter.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Value, next), ct);
            if (affected == 1) return Format(next);
            // Someone else incremented it between our read and write — retry with the new value.
        }

        throw new InvalidOperationException("Could not allocate a queue number after multiple attempts.");
    }

    private static string Format(int seq) => $"Q-{seq:D3}";
}
