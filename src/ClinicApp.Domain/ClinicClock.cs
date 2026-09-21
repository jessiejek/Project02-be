namespace ClinicApp.Domain;

/// <summary>§17.1 #3 — the clinic runs in Asia/Manila (UTC+8, no DST). Every business
/// date ("today's queue", a booking's appointment date, "recorded on") must be computed
/// in that zone, never from the UTC date: between 16:00–24:00 UTC (00:00–08:00 PHT) the
/// UTC date is still *yesterday* in Manila, so the queue / bookings / dashboards would
/// all query the wrong day.
///
/// This is the only place the server should turn an instant into a clinic date. Do not
/// use `DateTime.UtcNow.Date`, `DateOnly.FromDateTime(utc)` or `now.Date` for a business
/// date — call <see cref="Today"/> or <see cref="DateOf"/>.</summary>
public static class ClinicClock
{
    // Manila has never observed DST in the modern era; a fixed offset is safe and
    // avoids a tz-database lookup that differs across OSes ("Asia/Manila" vs
    // "Singapore Standard Time").
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>Time source. Always the system clock in production; the integration tests
    /// swap in a fixed instant to prove the 00:00–08:00 PHT window. Not thread-safe to
    /// change while requests are in flight — tests only.</summary>
    public static TimeProvider Time { get; set; } = TimeProvider.System;

    public static DateTimeOffset UtcNow => Time.GetUtcNow();

    public static DateTimeOffset Now => UtcNow.ToOffset(Offset);

    /// <summary>The Manila calendar date of any instant.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    /// <summary>The Manila wall-clock time of any instant.</summary>
    public static TimeOnly TimeOf(DateTimeOffset instant) => TimeOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    public static DateOnly Today => DateOf(UtcNow);

    public static TimeOnly TimeNow => TimeOf(UtcNow);
}
