// JJQR - AI (Nov 20, 2026) - B-04: a review needs a COMPLETED visit
using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class ReviewRulesTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<(HttpClient Client, Guid Booking)> BookingFor(BookingStatus status)
    {
        var p = await TestHelpers.NewPatientAsync(F);
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, p.PatientId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(b => b.BookingId == booking)).Status = status; await db.SaveChangesAsync(); return 0; });
        return (F.ClientFor(p.UserId, "Patient"), booking);
    }

    private static StringContent Review(Guid booking, string? comment = "ok") =>
        ApiFactory.Body(new { booking_id = booking, doctor_id = Guid.Empty, patient_id = Guid.Empty, rating = 5, comment });

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.CheckedIn)]
    [InlineData(BookingStatus.InProgress)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public async Task A_visit_that_is_not_completed_cannot_be_reviewed(BookingStatus status)
    {
        var (c, booking) = await BookingFor(status);
        using var _ = c;
        var res = await c.PostAsync("/api/reviews", Review(booking));
        await ShouldBe(HttpStatusCode.BadRequest, res);
        Assert.Contains("completed", await res.Content.ReadAsStringAsync());
        Assert.False(await WithDb(db => db.Reviews.AnyAsync(r => r.BookingId == booking)));
    }

    [Fact]
    public async Task A_completed_visit_can_be_reviewed_once_and_a_huge_comment_is_refused()
    {
        var (c, booking) = await BookingFor(BookingStatus.Completed);
        using var _ = c;
        await ShouldBe(HttpStatusCode.BadRequest, await c.PostAsync("/api/reviews", Review(booking, new string('x', 2001))));
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/reviews", Review(booking)));
        await ShouldBe(HttpStatusCode.Conflict, await c.PostAsync("/api/reviews", Review(booking)));
    }
}
// JJQR - END
