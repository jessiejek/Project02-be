//JJQR - AI (Nov 24, 2026) - Admin setting "Mark the visit paid when the doctor completes it" (clinic_settings.auto_paid_on_complete)
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class AutoPaidOnCompleteTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");

    private async Task EnsureSettings() => await WithDb(async db =>
    {
        if (!await db.ClinicSettings.AnyAsync(s => s.Id == 1))
        {
            db.ClinicSettings.Add(new ClinicSetting { Id = 1, ClinicName = "Test Clinic", Address = "Cebu", DefaultPaymentMode = PaymentMode.PayAtClinic, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        return 0;
    });

    private async Task<HttpClient> AdminAsync()
    {
        await EnsureSettings();
        var admin = await TestHelpers.NewStaffLoginAsync(F, UserRole.Admin);
        return F.ClientFor(admin.UserId, "Admin");
    }

    private Task SetFlag(bool on) => WithDb(async db =>
    {
        (await db.ClinicSettings.SingleAsync(s => s.Id == 1)).AutoPaidOnComplete = on;
        await db.SaveChangesAsync();
        return 0;
    });

    private async Task<Guid> InProgressBooking()
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(b => b.BookingId == booking)).Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        return booking;
    }

    private async Task Complete(Guid booking, string decision = "Charge")
    {
        using var doc = DocA();
        var body = new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Completed",
            ["chief_complaint"] = "cough", ["assessment"] = "URTI", ["visit_type"] = "New", ["pf_decision"] = decision,
            ["pf_amount"] = decision == "Charge" ? 450m : null, ["pf_waive_reason"] = decision == "Waive" ? "Family of staff" : null,
        };
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(body)));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{booking}/complete", null));
    }

    private Task<Payment> PaymentOf(Guid booking) => WithDb(db => db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == booking));

    [Fact]
    public async Task Off_by_default_the_visit_waits_for_the_front_desk()
    {
        await EnsureSettings();
        await SetFlag(false);
        var booking = await InProgressBooking();
        await Complete(booking);
        Assert.Equal(PaymentStatus.Unpaid, (await PaymentOf(booking)).Status);
    }

    [Fact]
    public async Task On_the_visit_is_marked_paid_cash_for_the_exact_fee()
    {
        await EnsureSettings();
        await SetFlag(true);
        try
        {
            var booking = await InProgressBooking();
            await Complete(booking);
            var p = await PaymentOf(booking);
            Assert.Equal(PaymentStatus.Paid, p.Status);
            Assert.Equal(PaymentMethod.Cash, p.PaymentMethod);
            Assert.Equal(p.Amount, p.AmountReceived);
            Assert.Null(p.OrNumber);
            Assert.NotNull(p.ConfirmedAt);
            Assert.Equal(0m, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.AmountDue).SingleAsync()));
        }
        finally { await SetFlag(false); }
    }

    [Fact]
    public async Task On_a_waived_visit_stays_waived()
    {
        await EnsureSettings();
        await SetFlag(true);
        try
        {
            var booking = await InProgressBooking();
            await Complete(booking, "Waive");
            Assert.Equal(PaymentStatus.Waived, (await PaymentOf(booking)).Status);
        }
        finally { await SetFlag(false); }
    }

    [Fact]
    public async Task Only_the_admin_can_switch_it_and_the_full_settings_save_never_changes_it()
    {
        using var admin = await AdminAsync();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.Forbidden, await doc.PutAsync("/api/settings/auto-paid-on-complete", ApiFactory.Body(new { enabled = true })));
        try
        {
            await ShouldBe(HttpStatusCode.OK, await admin.PutAsync("/api/settings/auto-paid-on-complete", ApiFactory.Body(new { enabled = true })));
            Assert.True((await JsonOf(await admin.GetAsync("/api/settings"))).GetProperty("auto_paid_on_complete").GetBoolean());

            // an older app saves the whole settings form without the new field: the switch stays on
            var s = await WithDb(db => db.ClinicSettings.AsNoTracking().SingleAsync(x => x.Id == 1));
            await ShouldBe(HttpStatusCode.OK, await admin.PutAsync("/api/settings", ApiFactory.Body(new Dictionary<string, object?>
            {
                ["clinic_name"] = s.ClinicName, ["address"] = s.Address, ["default_payment_mode"] = s.DefaultPaymentMode.ToString(), ["consent_version"] = s.ConsentVersion,
                ["fee_consultation"] = s.FeeConsultation, ["fee_follow_up"] = s.FeeFollowUp, ["fee_senior_pwd"] = s.FeeSeniorPwd, ["fee_med_cert"] = s.FeeMedCert, ["discount_pct"] = s.DiscountPct,
            })));
            Assert.True((await WithDb(db => db.ClinicSettings.AsNoTracking().SingleAsync(x => x.Id == 1))).AutoPaidOnComplete);
        }
        finally { await SetFlag(false); }
    }
}
//JJQR - END
