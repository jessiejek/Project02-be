using System.Net;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

/// <summary>The booking state machine must allow every transition the real UI performs (queue page,
/// doctor "Complete Consultation", staff Undo Check-In, admin booking page) and still reject the
/// nonsense ones. Each InlineData row below is a button someone can actually press.</summary>
[Collection("api")]
public class BookingFlowTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<Guid> Booking(BookingStatus status)
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            (await db.Bookings.SingleAsync(b => b.BookingId == booking)).Status = status;
            await db.SaveChangesAsync();
            return 0;
        });
        return booking;
    }

    private Task<BookingStatus> StatusOf(Guid booking) =>
        WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync());

    // ── /staff/queue page buttons, the doctor's Complete, staff/admin booking pages ──────────
    [Theory]
    // Queue page — Pending row: Check in, No-show
    [InlineData(BookingStatus.Pending, "check-in", BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.Pending, "no-show", BookingStatus.NoShow)]
    // Queue page — CheckedIn / OnHold rows: Call, Hold, No-show
    [InlineData(BookingStatus.CheckedIn, "call", BookingStatus.InProgress)]
    [InlineData(BookingStatus.CheckedIn, "hold", BookingStatus.OnHold)]
    [InlineData(BookingStatus.CheckedIn, "no-show", BookingStatus.NoShow)]
    [InlineData(BookingStatus.OnHold, "call", BookingStatus.InProgress)]
    [InlineData(BookingStatus.OnHold, "hold", BookingStatus.OnHold)]
    [InlineData(BookingStatus.OnHold, "no-show", BookingStatus.NoShow)]
    // Queue page — InProgress row: Complete, Hold, No-show
    [InlineData(BookingStatus.InProgress, "complete", BookingStatus.Completed)]
    [InlineData(BookingStatus.InProgress, "hold", BookingStatus.OnHold)]
    [InlineData(BookingStatus.InProgress, "no-show", BookingStatus.NoShow)]
    // Doctor "Complete Consultation" → queue complete, even if nobody pressed Call first
    [InlineData(BookingStatus.CheckedIn, "complete", BookingStatus.Completed)]
    [InlineData(BookingStatus.OnHold, "complete", BookingStatus.Completed)]
    // …and pressing it twice is harmless
    [InlineData(BookingStatus.Completed, "complete", BookingStatus.Completed)]
    public async Task Queue_actions_the_ui_offers_are_allowed(BookingStatus from, string action, BookingStatus to)
    {
        var booking = await Booking(from);
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/queue/{booking}/{action}", null));
        Assert.Equal(to, await StatusOf(booking));
    }

    [Fact]
    public async Task The_doctor_can_complete_their_own_queue_entry_straight_from_CheckedIn()
    {
        var booking = await Booking(BookingStatus.CheckedIn);
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{booking}/complete", null));
        Assert.Equal(BookingStatus.Completed, await StatusOf(booking));
    }

    [Theory]
    // Staff booking page / staff dashboard: Check In ⇄ Undo Check-In
    [InlineData(BookingStatus.Confirmed, BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.Confirmed)]
    // Admin booking page
    [InlineData(BookingStatus.Pending, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.ProofSubmitted, BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Completed)]   // "Mark Complete"
    [InlineData(BookingStatus.Confirmed, BookingStatus.NoShow)]      // "Mark No Show"
    [InlineData(BookingStatus.Confirmed, BookingStatus.Rescheduled)] // "Reschedule"
    [InlineData(BookingStatus.Confirmed, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Pending, BookingStatus.Cancelled)]     // "Reject Booking"
    [InlineData(BookingStatus.ProofSubmitted, BookingStatus.Cancelled)] // "Reject Proof"
    public async Task Status_changes_the_booking_pages_offer_are_allowed(BookingStatus from, BookingStatus to)
    {
        var booking = await Booking(from);
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = to.ToString(), reason = "test" })));
        Assert.Equal(to, await StatusOf(booking));
    }

    // ── still rejected ───────────────────────────────────────────────────
    [Theory]
    [InlineData(BookingStatus.Pending, "complete")]     // skips check-in / the consultation
    [InlineData(BookingStatus.Pending, "call")]
    [InlineData(BookingStatus.Completed, "call")]
    [InlineData(BookingStatus.Completed, "hold")]
    [InlineData(BookingStatus.Completed, "no-show")]    // a finished visit can't become a no-show
    [InlineData(BookingStatus.NoShow, "complete")]
    [InlineData(BookingStatus.NoShow, "call")]
    [InlineData(BookingStatus.Cancelled, "call")]
    [InlineData(BookingStatus.Cancelled, "complete")]
    [InlineData(BookingStatus.Expired, "check-in")]
    public async Task Nonsense_queue_actions_are_rejected_and_change_nothing(BookingStatus from, string action)
    {
        var booking = await Booking(from);
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PutAsync($"/api/queue/{booking}/{action}", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(from, await StatusOf(booking));
    }

    [Theory]
    [InlineData(BookingStatus.Completed, BookingStatus.Pending)]
    [InlineData(BookingStatus.Completed, BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.Completed, BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.NoShow, BookingStatus.Completed)]
    [InlineData(BookingStatus.Pending, BookingStatus.Completed)]
    [InlineData(BookingStatus.Pending, BookingStatus.InProgress)]
    public async Task Nonsense_status_jumps_are_rejected_and_change_nothing(BookingStatus from, BookingStatus to)
    {
        var booking = await Booking(from);
        using var admin = F.As(W.Admin, "Admin");
        var res = await admin.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = to.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(from, await StatusOf(booking));
    }

    // ── machine-level table for the transitions above ────────────────────
    [Theory]
    [InlineData(BookingStatus.Pending, BookingStatus.NoShow, true)]
    [InlineData(BookingStatus.InProgress, BookingStatus.NoShow, true)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.Completed, true)]
    [InlineData(BookingStatus.OnHold, BookingStatus.Completed, true)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Confirmed, BookingStatus.Completed, true)]
    [InlineData(BookingStatus.Pending, BookingStatus.Completed, false)]
    [InlineData(BookingStatus.Completed, BookingStatus.NoShow, false)]
    public void Machine_table(BookingStatus from, BookingStatus to, bool ok) =>
        Assert.Equal(ok, BookingStatusMachine.CanTransition(from, to));
}
