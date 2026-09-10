namespace ClinicApp.Domain;

/// <summary>§17.1 #3 — the clinic runs in Asia/Manila (UTC+8, no DST). "Today"
/// must be computed in that zone, not UTC, or between 16:00–24:00 UTC (00:00–08:00
/// PHT) the queue / bookings / dashboards all query yesterday.</summary>
public static class ClinicClock
{
    // Manila has never observed DST in the modern era; a fixed offset is safe and
    // avoids a tz-database lookup that differs across OSes ("Asia/Manila" vs
    // "Singapore Standard Time").
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Offset);

    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public static TimeOnly TimeNow => TimeOnly.FromDateTime(Now.DateTime);
}
