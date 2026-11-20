// JJQR - AI (Nov 20, 2026) - B-06 blocked dates / doctor weekdays, B-29 clinic closed days, B-05 double booking
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class BookingRulesTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<HttpResponseMessage> Book(HttpClient c, DateOnly date) =>
        await c.PostAsync("/api/bookings/book", ApiFactory.Body(new { appointment_date = date.ToString("yyyy-MM-dd") }));

    private async Task<HttpClient> NewPatientClient() =>
        F.ClientFor((await TestHelpers.NewPatientAsync(F)).UserId, "Patient");

    // ── B-05 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Same_patient_cannot_book_the_same_date_twice_but_can_book_another_date()
    {
        using var c = await NewPatientClient();
        var d = TestHelpers.FutureDate(DayOfWeek.Tuesday);
        await ShouldBe(HttpStatusCode.Created, await Book(c, d));
        var again = await Book(c, d);
        await ShouldBe(HttpStatusCode.Conflict, again);
        Assert.Contains("already have a booking", await again.Content.ReadAsStringAsync());
        await ShouldBe(HttpStatusCode.Created, await Book(c, d.AddDays(1)));
    }

    [Fact]
    public async Task A_cancelled_booking_frees_the_date()
    {
        using var c = await NewPatientClient();
        var d = TestHelpers.FutureDate(DayOfWeek.Wednesday);
        var first = await JsonOf(await Book(c, d));
        await ShouldBe(HttpStatusCode.OK, await c.PutAsync($"/api/bookings/{first.GetProperty("booking_id").GetGuid()}/cancel", ApiFactory.Body(new { reason = "changed my mind" })));
        await ShouldBe(HttpStatusCode.Created, await Book(c, d));
    }

    [Fact]
    public async Task Two_different_patients_can_book_the_same_date()
    {
        using var a = await NewPatientClient();
        using var b = await NewPatientClient();
        var d = TestHelpers.FutureDate(DayOfWeek.Thursday);
        await ShouldBe(HttpStatusCode.Created, await Book(a, d));
        await ShouldBe(HttpStatusCode.Created, await Book(b, d));
    }

    // ── B-06 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Booking_a_date_the_doctor_blocked_is_refused()
    {
        var doctor = await TestHelpers.BookingDoctorAsync(F);
        var d = TestHelpers.FutureDate(DayOfWeek.Friday);
        var blockId = Guid.NewGuid();
        await WithDb(async db => { db.DoctorBlockedDates.Add(new DoctorBlockedDate { Id = blockId, DoctorId = doctor, BlockedDate = d, Reason = "conference", CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); return 0; });
        try
        {
            using var c = await NewPatientClient();
            var res = await Book(c, d);
            await ShouldBe(HttpStatusCode.BadRequest, res);
            Assert.Contains("not available", await res.Content.ReadAsStringAsync());
            // the next day is fine
            await ShouldBe(HttpStatusCode.Created, await Book(c, d.AddDays(7)));
        }
        finally
        {
            await WithDb(async db => { db.DoctorBlockedDates.Remove(await db.DoctorBlockedDates.SingleAsync(x => x.Id == blockId)); await db.SaveChangesAsync(); return 0; });
        }
    }

    [Fact]
    public async Task Booking_a_weekday_the_doctor_marked_off_is_refused()
    {
        var doctor = await TestHelpers.BookingDoctorAsync(F);
        var d = TestHelpers.FutureDate(DayOfWeek.Saturday);
        var rowId = Guid.NewGuid();
        await WithDb(async db => { db.DoctorSchedules.Add(new DoctorSchedule { Id = rowId, DoctorId = doctor, DayOfWeek = (short)d.DayOfWeek, IsActive = false, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); return 0; });
        try
        {
            using var c = await NewPatientClient();
            await ShouldBe(HttpStatusCode.BadRequest, await Book(c, d));
        }
        finally
        {
            await WithDb(async db => { db.DoctorSchedules.Remove(await db.DoctorSchedules.SingleAsync(x => x.Id == rowId)); await db.SaveChangesAsync(); return 0; });
        }
    }

    [Fact]
    public async Task A_doctor_with_no_schedule_rows_is_not_restricted_by_weekday()
    {
        using var c = await NewPatientClient();
        await ShouldBe(HttpStatusCode.Created, await Book(c, TestHelpers.FutureDate(DayOfWeek.Saturday, 2)));
    }

    // ── B-29 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Booking_on_a_day_the_clinic_is_closed_is_refused_and_staff_scheduling_too()
    {
        var d = TestHelpers.FutureDate(DayOfWeek.Sunday);
        await WithDb(async db => { db.ClinicOperatingHours.RemoveRange(db.ClinicOperatingHours.Where(h => h.DayOfWeek == 0)); db.ClinicOperatingHours.Add(new ClinicOperatingHour { DayOfWeek = 0, IsClosed = true }); await db.SaveChangesAsync(); return 0; });
        try
        {
            using var c = await NewPatientClient();
            var res = await Book(c, d);
            await ShouldBe(HttpStatusCode.BadRequest, res);
            Assert.Contains("closed", await res.Content.ReadAsStringAsync());

            // staff booking on the patient's behalf follows the same rule; a walk-in does not (they're at the desk)
            var p = await TestHelpers.NewPatientAsync(F);
            var doctor = await TestHelpers.BookingDoctorAsync(F);
            using var staff = F.As(W.Staff, "Staff");
            object Body(bool walkIn) => new { patient_id = p.PatientId, doctor_id = doctor, appointment_date = d.ToString("yyyy-MM-dd"), slot_start_time = "09:00:00", slot_end_time = "09:15:00", service_ids = Array.Empty<Guid>(), payment_mode = "PayAtClinic", is_walk_in = walkIn };
            await ShouldBe(HttpStatusCode.BadRequest, await staff.PostAsync("/api/bookings", ApiFactory.Body(Body(false))));
            await ShouldBe(HttpStatusCode.Created, await staff.PostAsync("/api/bookings", ApiFactory.Body(Body(true))));
        }
        finally
        {
            await WithDb(async db => { db.ClinicOperatingHours.RemoveRange(db.ClinicOperatingHours.Where(h => h.DayOfWeek == 0)); await db.SaveChangesAsync(); return 0; });
        }
    }

    [Fact]
    public async Task Open_days_still_book_fine()
    {
        using var c = await NewPatientClient();
        await ShouldBe(HttpStatusCode.Created, await Book(c, TestHelpers.FutureDate(DayOfWeek.Monday, 3)));
    }
}
// JJQR - END
