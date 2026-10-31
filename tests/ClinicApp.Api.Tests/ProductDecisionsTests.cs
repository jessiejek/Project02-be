using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Oct 31, 2026)
[Collection("api")]
public class ProductDecisionsTests(ApiFixture api) : ApiTestBase(api)
{
    [Fact]
    public async Task Patient_can_cancel_own_pending_booking()
    {
        var booking = await WithDb(async db =>
        {
            var (id, _) = await World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId);
            var row = await db.Bookings.SingleAsync(b => b.BookingId == id);
            row.Status = BookingStatus.Pending;
            await db.SaveChangesAsync();
            return id;
        });

        using var patient = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.OK, await patient.PutAsync($"/api/bookings/{booking}/cancel", ApiFactory.Body(new { reason = "plans changed" })));
        Assert.Equal(BookingStatus.Cancelled, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));
    }

    [Fact]
    public async Task Reschedule_marks_old_and_clones_new_booking()
    {
        var booking = await WithDb(async db =>
        {
            var (id, _) = await World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId);
            var row = await db.Bookings.SingleAsync(b => b.BookingId == id);
            row.Status = BookingStatus.Confirmed;
            await db.SaveChangesAsync();
            return id;
        });

        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PutAsync($"/api/bookings/{booking}/reschedule", ApiFactory.Body(new { appointment_date = "2099-06-15", reason = "clinic closed" }));
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await JsonOf(res);
        var newId = body.GetProperty("new_booking_id").GetGuid();
        Assert.Equal(BookingStatus.Rescheduled, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));
        var newRow = await WithDb(db => db.Bookings.SingleAsync(b => b.BookingId == newId));
        Assert.Equal(BookingStatus.Confirmed, newRow.Status);
        Assert.Equal(DateOnly.Parse("2099-06-15"), newRow.AppointmentDate);
        Assert.NotNull(newRow.QueueNumber);
    }

    [Fact]
    public async Task Reschedule_rejects_illegal_transition()
    {
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PutAsync($"/api/bookings/{W.BookingA}/reschedule", ApiFactory.Body(new { appointment_date = "2099-06-15" }));
        await ShouldBe(HttpStatusCode.BadRequest, res);
    }

    [Fact]
    public async Task Global_search_scopes_patient_bookings_to_self()
    {
        using var patient = F.Patient(W.PatientA);
        var res = await patient.GetAsync("/api/search?q=Test");
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await JsonOf(res);
        foreach (var b in body.GetProperty("bookings").EnumerateArray())
            Assert.Equal(W.PatientA.RowId, b.GetProperty("patient_id").GetGuid());
        Assert.DoesNotContain(body.GetProperty("patients").EnumerateArray(), _ => true);
    }

    [Fact]
    public async Task Global_search_returns_patients_for_staff()
    {
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.GetAsync("/api/search?q=Alice");
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await JsonOf(res);
        Assert.NotEmpty(body.GetProperty("patients").EnumerateArray());
    }

    [Fact]
    public async Task Replace_allergies_persists_for_doctor()
    {
        using var doc = F.As(W.DoctorA, "Doctor");
        var res = await doc.PutAsync($"/api/patients/{W.PatientA.RowId}/allergies", ApiFactory.Body(new[]
        {
            new { allergen = "Penicillin", reaction = (string?)null, severity = (string?)null, is_nkda = false },
        }));
        await ShouldBe(HttpStatusCode.OK, res);
        var rows = await ListOf(res);
        Assert.Contains(rows, r => r.GetProperty("allergen").GetString() == "Penicillin");
    }
}
//JJQR - END
