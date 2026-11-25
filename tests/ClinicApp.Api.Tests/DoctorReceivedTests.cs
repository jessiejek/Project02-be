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

    //JJQR - AI (Nov 25, 2026) - code review: two requests at the same time (double tap / two devices) → exactly one Paid, one 200, one audit row
    [Fact]
    public async Task Two_requests_at_once_record_the_payment_once()
    {
        var booking = await CompletedUnpaid();
        using var a = DocA();
        using var b = DocA();
        var results = await Task.WhenAll(
            a.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })),
            b.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })),
            a.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var p = await PaymentOf(booking);
        Assert.Equal(PaymentStatus.Paid, p.Status);
        Assert.Single(await WithDb(db => db.AuditLogs.AsNoTracking().Where(x => x.EntityId == p.PaymentId && x.Action == "Paid (marked by doctor)").ToListAsync()));
    }

    [Fact]
    public async Task The_sync_row_version_moves_by_one_like_any_other_save()
    {
        var booking = await CompletedUnpaid();
        var before = (await PaymentOf(booking)).RowVersion;
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        Assert.Equal(before + 1, (await PaymentOf(booking)).RowVersion);
    }
    //JJQR - END

    //JJQR - AI (Nov 25, 2026) - owner ("just trap the 2nd transaction"): staff Record payment uses the same guard; the second save gets 409 and changes nothing
    private HttpClient StaffClient() => F.As(W.Staff, "Staff");
    private static StringContent StaffCash() => ApiFactory.Body(new { payment_method = "Cash", amount_received = 450m });

    [Fact]
    public async Task Staff_record_payment_and_doctor_mark_received_at_once_record_it_once()
    {
        for (var round = 0; round < 6; round++)
        {
            var booking = await CompletedUnpaid();
            var pay = (await PaymentOf(booking)).PaymentId;
            using var staff = StaffClient();
            using var doc = DocA();
            var staffFirst = round % 2 == 0;
            var calls = staffFirst
                ? new[] { staff.PostAsync($"/api/payments/{pay}/confirm", StaffCash()), doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })) }
                : new[] { doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })), staff.PostAsync($"/api/payments/{pay}/confirm", StaffCash()) };
            var results = await Task.WhenAll(calls);
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            var loser = results.Single(r => r.StatusCode != HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Contains("no longer unpaid", await loser.Content.ReadAsStringAsync());
            var p = await PaymentOf(booking);
            Assert.Equal(PaymentStatus.Paid, p.Status);
            var doctorWon = results[staffFirst ? 1 : 0].StatusCode == HttpStatusCode.OK;
            var doctorAudits = await WithDb(db => db.AuditLogs.AsNoTracking().CountAsync(x => x.EntityId == p.PaymentId && x.Action == "Paid (marked by doctor)"));
            Assert.Equal(doctorWon ? 1 : 0, doctorAudits); // one audit row when the doctor's save won, none from the losing request
            Assert.Equal(doctorWon ? W.DoctorA.UserId : W.Staff.UserId, p.ConfirmedByUserId);
            Assert.Equal(doctorWon, p.ConfirmNotes == ClinicApp.Api.Controllers.PaymentsController.DoctorReceivedNote);
        }
    }

    [Fact]
    public async Task Two_staff_record_payments_at_once_record_it_once()
    {
        var booking = await CompletedUnpaid();
        var pay = (await PaymentOf(booking)).PaymentId;
        using var a = StaffClient();
        using var b = StaffClient();
        var results = await Task.WhenAll(a.PostAsync($"/api/payments/{pay}/confirm", StaffCash()), b.PostAsync($"/api/payments/{pay}/confirm", StaffCash()), a.PostAsync($"/api/payments/{pay}/confirm", StaffCash()));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(PaymentStatus.Paid, (await PaymentOf(booking)).Status);
    }

    [Fact]
    public async Task Staff_record_payment_after_the_doctor_marked_it_received_is_refused_and_changes_nothing()
    {
        var booking = await CompletedUnpaid();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync(Url(booking), ApiFactory.Body(new { amount = 450m })));
        var before = await PaymentOf(booking);
        using var staff = StaffClient();
        var res = await staff.PostAsync($"/api/payments/{before.PaymentId}/confirm", ApiFactory.Body(new { payment_method = "GCash", amount_received = 450m, reference_number = "R9", or_number = "OR-TRAP-1" }));
        await ShouldBe(HttpStatusCode.Conflict, res);
        Assert.Contains("This visit is no longer unpaid (it was just recorded).", await res.Content.ReadAsStringAsync());
        var after = await PaymentOf(booking);
        Assert.Equal(before.PaymentMethod, after.PaymentMethod);
        Assert.Null(after.OrNumber);
        Assert.Null(after.ReferenceNumber);
        Assert.Equal(before.ConfirmedByUserId, after.ConfirmedByUserId);
        Assert.Equal(before.RowVersion, after.RowVersion);
    }

    [Fact]
    public async Task Staff_record_payment_still_works_once_and_moves_the_sync_row_version_by_one()
    {
        var booking = await CompletedUnpaid();
        var before = await PaymentOf(booking);
        using var staff = StaffClient();
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{before.PaymentId}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 450m, or_number = "OR-ONCE-1" })));
        var after = await PaymentOf(booking);
        Assert.Equal(PaymentStatus.Paid, after.Status);
        Assert.Equal("OR-ONCE-1", after.OrNumber);
        Assert.Equal(W.Staff.UserId, after.ConfirmedByUserId);
        Assert.Equal(before.RowVersion + 1, after.RowVersion);
        await ShouldBe(HttpStatusCode.Conflict, await staff.PostAsync($"/api/payments/{before.PaymentId}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 450m })));
        Assert.Equal("OR-ONCE-1", (await PaymentOf(booking)).OrNumber);
    }
    //JJQR - END
}
//JJQR - END
