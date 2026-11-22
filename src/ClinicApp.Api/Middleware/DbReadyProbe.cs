//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.2: "does the database answer?" that ALWAYS returns within the time limit.
// The EF connection retries and waits ~15 s when SQL Server is down, so a cancellation token alone does not stop it. The probe runs on its OWN scope in a background
// task; the caller waits at most the limit and reports "not ok" when it has not answered. Only one probe runs at a time, so polling cannot pile up connection attempts.
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Middleware;

public static class DbReadyProbe
{
    private static readonly object Gate = new();
    private static Task<bool>? _running;

    public static async Task<(bool Ok, int Ms)> CheckAsync(IServiceScopeFactory scopes, TimeSpan limit, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Task<bool> probe;
        lock (Gate)
        {
            if (_running is null || _running.IsCompleted) _running = Task.Run(() => ProbeAsync(scopes));
            probe = _running;
        }
        var finished = await Task.WhenAny(probe, Task.Delay(limit, ct));
        var ok = finished == probe && await probe;
        sw.Stop();
        return (ok, (int)sw.ElapsedMilliseconds);
    }

    private static async Task<bool> ProbeAsync(IServiceScopeFactory scopes)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            return true;
        }
        catch { return false; }
    }
}
//JJQR - END
