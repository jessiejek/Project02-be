using System.Globalization;
using System.Net;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

/// <summary>A fixed instant for <see cref="ClinicClock"/>. Restores the system clock on dispose.
/// Only used from the sequential "api" collection (the clock is process-global).</summary>
public sealed class FrozenClock : TimeProvider, IDisposable
{
    private readonly DateTimeOffset _utc;
    private readonly TimeProvider _previous = ClinicClock.Time;

    private FrozenClock(DateTimeOffset utc)
    {
        _utc = utc;
        ClinicClock.Time = this;
    }

    public static FrozenClock At(string utcIso) => new(DateTimeOffset.Parse(utcIso, null, DateTimeStyles.AssumeUniversal));
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Dispose() => ClinicClock.Time = _previous;
}

/// <summary>P0.4 — pure clock maths. The clinic date is Asia/Manila (UTC+8), so from 16:00 UTC the
/// clinic is already on the next calendar day. These would all pass on a UTC machine *only* because
/// the helper converts; the last theory shows the naive UTC date is wrong for every one of them.</summary>
public class ClinicClockTests
{
    [Theory]
    // instant (UTC)              → Manila date, Manila time
    [InlineData("2026-09-21T00:00:00Z", "2026-09-21", "08:00:00")] // 08:00 PHT — UTC and PHT agree from here to 16:00Z
    [InlineData("2026-09-21T12:00:00Z", "2026-09-21", "20:00:00")]
    [InlineData("2026-09-21T15:59:59Z", "2026-09-21", "23:59:59")] // last second of the PHT day
    [InlineData("2026-09-21T16:00:00Z", "2026-09-22", "00:00:00")] // PHT midnight — UTC is still the 21st
    [InlineData("2026-09-21T16:00:01Z", "2026-09-22", "00:00:01")]
    [InlineData("2026-09-21T20:30:00Z", "2026-09-22", "04:30:00")] // "before 08:00 PHT" — the bug window
    [InlineData("2026-09-21T23:59:59Z", "2026-09-22", "07:59:59")] // last second before 08:00 PHT
    [InlineData("2026-12-31T16:00:00Z", "2027-01-01", "00:00:00")] // year rollover
    [InlineData("2026-09-30T18:00:00Z", "2026-10-01", "02:00:00")] // month rollover
    [InlineData("2028-02-28T16:00:00Z", "2028-02-29", "00:00:00")] // leap day
    public void Manila_date_and_time_of_an_instant(string utc, string date, string time)
    {
        var instant = DateTimeOffset.Parse(utc, null, DateTimeStyles.AssumeUniversal);
        Assert.Equal(DateOnly.Parse(date), ClinicClock.DateOf(instant));
        Assert.Equal(TimeOnly.Parse(time), ClinicClock.TimeOf(instant));
    }

    [Theory]
    [InlineData("2026-09-21T16:00:00Z")]
    [InlineData("2026-09-21T20:30:00Z")]
    [InlineData("2026-09-21T23:59:59Z")]
    public void Before_0800_PHT_the_naive_utc_date_is_yesterday_and_the_clock_does_not_use_it(string utc)
    {
        var instant = DateTimeOffset.Parse(utc, null, DateTimeStyles.AssumeUniversal);
        var naiveUtc = DateOnly.FromDateTime(instant.UtcDateTime);
        Assert.Equal(naiveUtc.AddDays(1), ClinicClock.DateOf(instant)); // the bug window: UTC is a day behind
    }

    [Fact]
    public void An_instant_in_any_offset_gives_the_same_manila_date()
    {
        var utc = DateTimeOffset.Parse("2026-09-21T20:30:00Z", null, DateTimeStyles.AssumeUniversal);
        Assert.Equal(ClinicClock.DateOf(utc), ClinicClock.DateOf(utc.ToOffset(TimeSpan.FromHours(-7))));
        Assert.Equal(ClinicClock.DateOf(utc), ClinicClock.DateOf(utc.ToOffset(TimeSpan.FromHours(8))));
    }
}

/// <summary>P0.4 — the API resolves "today" in Manila. Time is frozen at 2026-09-21T20:30Z, i.e.
/// 04:30 on 2026-09-22 in Manila: UTC still says the 21st, the clinic is on the 22nd.</summary>
[Collection("api")]
public class ManilaTodayApiTests(ApiFixture api) : ApiTestBase(api)
{
    private const string BugWindow = "2026-09-21T20:30:00Z";   // 04:30 PHT on the 22nd
    private static readonly DateOnly ManilaToday = new(2026, 9, 22);
    private static readonly DateOnly UtcToday = new(2026, 9, 21);

    private async Task<Guid> AddBooking(DateOnly date, Guid patientId, BookingStatus status = BookingStatus.CheckedIn)
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Bookings.Add(new Booking
            {
                BookingId = id, PatientId = patientId, DoctorId = W.DoctorA.RowId, AppointmentDate = date,
                Status = status, PaymentMode = PaymentMode.PayAtClinic, QueueNumber = "Q-" + id.ToString()[..4],
                ConsultationFeeSnapshot = 450, TotalFee = 450, AmountDue = 450, CreatedAt = now, UpdatedAt = now
            });
            db.Payments.Add(new Payment { PaymentId = Guid.NewGuid(), BookingId = id, Amount = 450, Status = PaymentStatus.Unpaid, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        });
        return id;
    }

    private Task EnsureSettings() => WithDb(async db =>
    {
        if (!await db.ClinicSettings.AnyAsync(s => s.Id == 1))
        {
            db.ClinicSettings.Add(new ClinicSetting { Id = 1, ClinicName = "Test Clinic", Address = "Cebu", DefaultPaymentMode = PaymentMode.PayAtClinic, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        return 0;
    });

    [Fact]
    public async Task Queue_defaults_to_the_manila_date_not_the_utc_date()
    {
        using var _ = FrozenClock.At(BugWindow);
        using var staff = F.As(W.Staff, "Staff");
        var body = await JsonOf(await staff.GetAsync("/api/queue"));
        Assert.Equal("2026-09-22", body.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Queue_shows_todays_manila_bookings_and_not_the_utc_days()
    {
        using var _ = FrozenClock.At(BugWindow);
        var today = await AddBooking(ManilaToday, W.PatientA.RowId);
        var yesterday = await AddBooking(UtcToday, W.PatientB.RowId);

        using var staff = F.As(W.Staff, "Staff");
        var items = (await JsonOf(await staff.GetAsync("/api/queue"))).GetProperty("items").EnumerateArray().ToList();
        Assert.True(AnyValue(items, "booking_id", today));
        Assert.False(AnyValue(items, "booking_id", yesterday));
    }

    [Fact]
    public async Task Staff_today_list_and_doctor_today_use_the_manila_date()
    {
        using var _ = FrozenClock.At(BugWindow);
        var today = await AddBooking(ManilaToday, W.PatientA.RowId);
        var yesterday = await AddBooking(UtcToday, W.PatientA.RowId);

        using var staff = F.As(W.Staff, "Staff");
        var staffToday = await ListOf(await staff.GetAsync("/api/bookings/staff/today?pageSize=500"));
        Assert.True(AnyValue(staffToday, "booking_id", today));
        Assert.False(AnyValue(staffToday, "booking_id", yesterday));

        using var doc = F.As(W.DoctorA, "Doctor");
        var docToday = await ListOf(await doc.GetAsync("/api/bookings/doctor/today"));
        Assert.True(AnyValue(docToday, "booking_id", today));
        Assert.False(AnyValue(docToday, "booking_id", yesterday));
        var summary = await JsonOf(await doc.GetAsync("/api/bookings/doctor/today-summary"));
        Assert.Equal(docToday.Count, summary.GetProperty("booked_today").GetInt32());
    }

    [Fact]
    public async Task Walk_in_check_in_lands_on_the_manila_date_and_starts_that_days_queue()
    {
        using var _ = FrozenClock.At(BugWindow);
        await EnsureSettings();
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PostAsync("/api/queue", ApiFactory.Body(new { patient_id = W.PatientA.RowId }));
        await ShouldBe(HttpStatusCode.OK, res);
        var bookingId = (await JsonOf(res)).GetProperty("booking_id").GetGuid();
        var row = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == bookingId));
        Assert.Equal(ManilaToday, row.AppointmentDate);
        Assert.Equal(new TimeOnly(4, 30), row.SlotStartTime); // arrival time is Manila wall-clock too
    }

    [Fact]
    public async Task Online_booking_defaults_to_manila_today_and_rejects_manilas_yesterday()
    {
        using var _ = FrozenClock.At(BugWindow);
        await EnsureSettings();
        using var a = F.Patient(W.PatientA);

        var ok = await a.PostAsync("/api/bookings/book", ApiFactory.Body(new { }));
        await ShouldBe(HttpStatusCode.Created, ok);
        Assert.Equal("2026-09-22", (await JsonOf(ok)).GetProperty("appointment_date").GetString());

        // The 21st is "today" in UTC but already the past in Manila.
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/bookings/book", ApiFactory.Body(new { appointment_date = "2026-09-21" })));
        await ShouldBe(HttpStatusCode.Created, await a.PostAsync("/api/bookings/book", ApiFactory.Body(new { appointment_date = "2026-09-22" })));
    }

    [Fact]
    public async Task Clinical_dates_recorded_by_the_server_are_manila_dates()
    {
        using var _ = FrozenClock.At(BugWindow);
        using var doc = F.As(W.DoctorA, "Doctor");

        // Vaccination "administered on"
        var vax = await doc.PutAsync($"/api/patient-vaccinations/by-consultation/{W.ConsultationA}", ApiFactory.Body(new[] { new { vaccine_name = "Flu" } }));
        var doses = await ListOf(vax);
        Assert.Contains(doses, d => d.GetProperty("administered_date").GetString() == "2026-09-22");

        // Medical certificate issue date defaults to today
        var cert = await doc.PutAsync($"/api/medical-certificates/by-consultation/{W.ConsultationA}",
            ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId }));
        await ShouldBe(HttpStatusCode.OK, cert);
        Assert.Equal("2026-09-22", (await JsonOf(cert)).GetProperty("issue_date").GetString());
    }

    [Fact]
    public async Task Vital_readings_are_stamped_with_the_manila_date()
    {
        using var _ = FrozenClock.At(BugWindow);
        var (templateId, bookingId) = (Guid.NewGuid(), Guid.NewGuid());
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.VitalFieldTemplates.Add(new VitalFieldTemplate { TemplateId = templateId, Description = "Temp " + templateId.ToString()[..4], FormKey = "temp_" + templateId.ToString("N"), Unit = "C", Icon = "x", CreatedAt = now });
            db.Bookings.Add(new Booking { BookingId = bookingId, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId, AppointmentDate = ManilaToday, Status = BookingStatus.CheckedIn, PaymentMode = PaymentMode.PayAtClinic, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");
        var rows = await ListOf(await staff.PutAsync($"/api/vitals/by-booking/{bookingId}", ApiFactory.Body(new[] { new { template_id = templateId, value = "36.8" } })));
        Assert.Equal("2026-09-22", rows.Single().GetProperty("recorded_at").GetString());
    }

    [Theory]
    [InlineData("2026-09-21T15:59:59Z", "2026-09-21")] // 23:59:59 PHT
    [InlineData("2026-09-21T16:00:00Z", "2026-09-22")] // 00:00:00 PHT
    public async Task The_queue_date_flips_exactly_at_manila_midnight(string utc, string expected)
    {
        using var _ = FrozenClock.At(utc);
        using var staff = F.As(W.Staff, "Staff");
        Assert.Equal(expected, (await JsonOf(await staff.GetAsync("/api/queue"))).GetProperty("date").GetString());
    }
}
