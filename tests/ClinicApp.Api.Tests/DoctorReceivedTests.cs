//JJQR - AI (Nov 25, 2026) - doctor dashboard "Mark received": POST /api/payments/booking/{bookingId}/doctor-received
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class DoctorReceivedTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");
    private static string Url(Guid booking) => $"/api/payments/booking/{booking}/doctor-received";

    private async Task<Guid> CompletedUnpaid(string decision = "Charge")
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(b => b.BookingId == booking)).Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        using var doc = DocA();
        var body = new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Completed",
            ["chief_complaint"] = "cough", ["assessment"] = "URTI", ["visit_type"] = "New", ["pf_decision"] = decision,
            ["pf_amount"] = decision == "Charge" ? 450m : null, ["pf_waive_reason"] = decision == "Waive" ? "Family of staff" : null,
        };
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(body)));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{booking}/complete", null));
        return booking;
    }

    private Task<Payment> PaymentOf(Guid booking) => WithDb(db => db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == booking));

    [Fact]
    public async Task Own_doctor_marks_the_exact_fee_received_in_cash_with_an_audit_entry()
    {
        var booking = await CompletedUnpaid();
        Assert.Equal(PaymentStatus.Unpaid, (await PaymentOf(booking)).Status);
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));

        var p = await PaymentOf(booking);
        Assert.Equal(PaymentStatus.Paid, p.Status);
        Assert.Equal(PaymentMethod.Cash, p.PaymentMethod);
        Assert.Equal(450m, p.AmountReceived);
        Assert.Equal(p.Amount, p.AmountReceived);
        Assert.Null(p.OrNumber);
        Assert.Equal(W.DoctorA.UserId, p.ConfirmedByUserId);
        Assert.NotNull(p.ConfirmedAt);
        Assert.Equal(0m, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.AmountDue).SingleAsync()));
        var audit = await WithDb(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == p.PaymentId).Select(a => a.Action).ToListAsync());
        Assert.Contains("Paid (marked by doctor)", audit);

        // a second tap (double tap / two devices) changes nothing and says so
        await ShouldBe(HttpStatusCode.Conflict, await doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        Assert.Single(await WithDb(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == p.PaymentId && a.Action == "Paid (marked by doctor)").ToListAsync()));
    }

    [Fact]
    public async Task Another_doctor_staff_admin_and_patient_cannot()
    {
        var booking = await CompletedUnpaid();
        using var docB = F.As(W.DoctorB, "Doctor");
        using var staff = F.As(W.Staff, "Staff");
        using var admin = F.As(W.Admin, "Admin");
        using var patient = F.As(W.PatientA, "Patient");
        await ShouldBe(HttpStatusCode.Forbidden, await docB.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        await ShouldBe(HttpStatusCode.Forbidden, await admin.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        await ShouldBe(HttpStatusCode.Forbidden, await patient.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        Assert.Equal(PaymentStatus.Unpaid, (await PaymentOf(booking)).Status);
    }

    [Fact]
    public async Task Refused_when_the_amount_changed()
    {
        using var doc = DocA();
        var booking = await CompletedUnpaid();
        await ShouldBe(HttpStatusCode.Conflict, await doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 500m })));
        Assert.Equal(PaymentStatus.Unpaid, (await PaymentOf(booking)).Status);
    }

    [Fact]
    public async Task Refused_when_the_fee_was_waived_or_the_visit_is_not_completed()
    {
        using var doc = DocA();
        var waived = await CompletedUnpaid("Waive");
        await ShouldBe(HttpStatusCode.Conflict, await doc.PostAsync(Url(waived), ApiFactory.Body(new { amount = 450m })));
        Assert.Equal(PaymentStatus.Waived, (await PaymentOf(waived)).Status);

        var (open, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(b => b.BookingId == open)).Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        await ShouldBe(HttpStatusCode.Conflict, await doc.PostAsync(Url(open), ApiFactory.Body(new { amount = 450m })));
        Assert.Equal(PaymentStatus.Unpaid, (await PaymentOf(open)).Status);
        await ShouldBe(HttpStatusCode.NotFound, await doc.PostAsync(Url(Guid.NewGuid()), ApiFactory.Body(new { amount = 450m })));
    }

    [Fact]
    public async Task No_body_marks_the_amount_due()
    {
        var booking = await CompletedUnpaid();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync(Url(booking), null));
        Assert.Equal(PaymentStatus.Paid, (await PaymentOf(booking)).Status);
    }
}
//JJQR - END
