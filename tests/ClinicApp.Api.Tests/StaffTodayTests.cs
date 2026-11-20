// JJQR - AI (Nov 20, 2026) - B-42: the front-desk dashboard list/count leaves out Cancelled rows
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class StaffTodayTests(ApiFixture api) : ApiTestBase(api)
{
    [Fact]
    public async Task ActiveOnly_leaves_out_cancelled_bookings_and_the_count_matches()
    {
        var (live, dead) = (Guid.NewGuid(), Guid.NewGuid());
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var (id, status) in new[] { (live, BookingStatus.Confirmed), (dead, BookingStatus.Cancelled) })
                db.Bookings.Add(new Booking { BookingId = id, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId, AppointmentDate = ClinicApp.Domain.ClinicClock.Today, Status = status, PaymentMode = PaymentMode.PayAtClinic, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");

        var all = await JsonOf(await staff.GetAsync("/api/bookings/staff/today?pageSize=500"));
        var ids = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("booking_id").GetGuid()).ToList();
        Assert.Contains(live, ids);
        Assert.Contains(dead, ids);

        var active = await JsonOf(await staff.GetAsync("/api/bookings/staff/today?pageSize=500&activeOnly=true"));
        var activeIds = active.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("booking_id").GetGuid()).ToList();
        Assert.Contains(live, activeIds);
        Assert.DoesNotContain(dead, activeIds);
        Assert.Equal(activeIds.Count, active.GetProperty("totalCount").GetInt32());
    }
}
// JJQR - END
