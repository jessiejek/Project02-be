using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 21, 2026) - the doctor's Waive decision must reach the payment (it used to stay "Unpaid" and the front desk was asked to collect it)
/// <summary>Completing a visit with a Waive decision waives its unpaid payment; drafts and Charge do not.</summary>
[Collection("api")]
public class WaivedFeeFlowTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");

    private async Task<(Guid Booking, Guid Payment)> NewVisit()
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            var b = await db.Bookings.SingleAsync(x => x.BookingId == booking);
            b.Status = BookingStatus.InProgress;
            await db.SaveChangesAsync();
            return 0;
        });
        return (booking, payment);
    }

    private StringContent Save(string status, string decision, string? reason = null, decimal? amount = null) =>
        ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId,
            ["status"] = status, ["chief_complaint"] = "cough", ["assessment"] = "URTI",
            ["visit_type"] = "New", ["pf_decision"] = decision, ["pf_waive_reason"] = reason, ["pf_amount"] = amount
        });

    private Task<(PaymentStatus Status, string? Reason, decimal AmountDue)> State(Guid booking, Guid payment) => WithDb(async db =>
    {
        var p = await db.Payments.AsNoTracking().SingleAsync(x => x.PaymentId == payment);
        var b = await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == booking);
        return (p.Status, p.WaivedReason, b.AmountDue);
    });

    [Fact]
    public async Task Completing_a_visit_with_a_waive_decision_waives_the_unpaid_payment()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", "Waive", "Friend of the doctor")));

        var s = await State(booking, payment);
        Assert.Equal(PaymentStatus.Waived, s.Status);
        Assert.Equal("Friend of the doctor", s.Reason);
        Assert.Equal(0m, s.AmountDue); // nothing left to collect, so it drops off the Payments page
    }

    [Fact]
    public async Task A_draft_with_a_waive_decision_does_not_waive_anything_yet()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Draft", "Waive", "Friend of the doctor")));

        Assert.Equal(PaymentStatus.Unpaid, (await State(booking, payment)).Status); // Waived cannot be undone, so only a finished visit does it
    }

    [Fact]
    public async Task A_charge_decision_leaves_the_payment_unpaid()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", "Charge", amount: 450m)));

        var s = await State(booking, payment);
        Assert.Equal(PaymentStatus.Unpaid, s.Status);
        Assert.True(s.AmountDue > 0);
    }

    [Fact]
    public async Task A_waive_without_a_reason_is_not_applied()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", "Waive")));

        Assert.Equal(PaymentStatus.Unpaid, (await State(booking, payment)).Status);
    }

    [Fact]
    public async Task An_already_paid_visit_is_never_waived_afterwards()
    {
        var (booking, payment) = await NewVisit();
        await WithDb(async db =>
        {
            (await db.Payments.SingleAsync(p => p.PaymentId == payment)).Status = PaymentStatus.Paid;
            await db.SaveChangesAsync();
            return 0;
        });
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", "Waive", "Friend of the doctor")));

        Assert.Equal(PaymentStatus.Paid, (await State(booking, payment)).Status);
    }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - what the doctor charges is what the front desk collects
/// <summary>The doctor's Charge amount becomes the payment amount and the amount due; settled money is never rewritten.</summary>
[Collection("api")]
public class DoctorChargeAmountTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");

    private async Task<(Guid Booking, Guid Payment)> NewVisit()
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.InProgress;
            await db.SaveChangesAsync();
            return 0;
        });
        return (booking, payment);
    }

    private StringContent Save(string status, decimal? amount, string decision = "Charge") =>
        ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId,
            ["status"] = status, ["chief_complaint"] = "cough", ["assessment"] = "URTI",
            ["visit_type"] = "New", ["pf_decision"] = decision, ["pf_amount"] = amount
        });

    private Task<(decimal PayAmount, decimal AmountDue, decimal TotalFee)> Amounts(Guid booking, Guid payment) => WithDb(async db =>
    {
        var p = await db.Payments.AsNoTracking().SingleAsync(x => x.PaymentId == payment);
        var b = await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == booking);
        return (p.Amount, b.AmountDue, b.TotalFee);
    });

    [Fact]
    public async Task The_amount_the_doctor_charges_is_the_amount_due_and_the_payment_amount()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", 300m)));

        var a = await Amounts(booking, payment);
        Assert.Equal(300m, a.PayAmount);
        Assert.Equal(300m, a.AmountDue);
        Assert.Equal(300m, a.TotalFee);
    }

    [Fact]
    public async Task Staff_cannot_record_less_than_the_doctors_amount_but_exactly_that_works()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", 300m)));

        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 299 })));
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 300 })));
    }

    [Fact]
    public async Task Without_a_doctor_amount_the_clinic_fee_is_used_and_payment_and_booking_agree()
    {
        var (booking, payment) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", null)));

        var a = await Amounts(booking, payment);
        Assert.True(a.AmountDue > 0);
        Assert.Equal(a.AmountDue, a.PayAmount); // the amount staff are checked against is the amount shown as due
    }

    [Fact]
    public async Task A_payment_already_made_is_never_rewritten_by_a_later_save()
    {
        var (booking, payment) = await NewVisit();
        await WithDb(async db =>
        {
            var p = await db.Payments.SingleAsync(x => x.PaymentId == payment);
            p.Status = PaymentStatus.Paid; p.Amount = 450m; p.AmountReceived = 450m;
            (await db.Bookings.SingleAsync(x => x.BookingId == booking)).AmountDue = 0m;
            await db.SaveChangesAsync();
            return 0;
        });
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", 300m)));

        var a = await Amounts(booking, payment);
        Assert.Equal(450m, a.PayAmount);
        Assert.Equal(0m, a.AmountDue); // a paid visit does not reappear as owing
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000000)]
    public async Task A_charge_amount_that_makes_no_sense_is_refused(double amount)
    {
        var (booking, _) = await NewVisit();
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Completed", (decimal)amount)));
    }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - self-healing: old records and the "record payment" step follow the doctor's decision
/// <summary>Records that predate the fix (waived by the doctor but still Unpaid, charge amount ignored) are healed, and staff can't collect against them.</summary>
[Collection("api")]
public class FeeDecisionReconcilerTests(ApiFixture api) : ApiTestBase(api)
{
    /// <summary>A finished visit in the exact stale state the app used to leave behind.</summary>
    private async Task<(Guid Booking, Guid Payment)> LegacyVisit(string? decision, string? reason, decimal? pfAmount, decimal paymentAmount, decimal amountDue, decimal totalFee)
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            var b = await db.Bookings.SingleAsync(x => x.BookingId == booking);
            b.Status = BookingStatus.Completed; b.AmountDue = amountDue; b.TotalFee = totalFee;
            var p = await db.Payments.SingleAsync(x => x.PaymentId == payment);
            p.Amount = paymentAmount; p.Status = PaymentStatus.Unpaid;
            if (decision is not null)
            {
                var now = DateTimeOffset.UtcNow;
                db.Consultations.Add(new ClinicApp.Domain.Entities.Consultation
                {
                    ConsultationId = Guid.NewGuid(), BookingId = booking, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId,
                    Status = ConsultationStatus.Completed, ChiefComplaint = "cough", PfDecision = decision, PfWaiveReason = reason, PfAmount = pfAmount,
                    CompletedAt = now, CreatedAt = now, UpdatedAt = now
                });
            }
            await db.SaveChangesAsync();
            return 0;
        });
        return (booking, payment);
    }

    private Task<ClinicApp.Api.Billing.FeeDecisionReconciler.Result> Run(Guid booking) => WithDb(async db =>
    {
        var r = await ClinicApp.Api.Billing.FeeDecisionReconciler.ReconcileAsync(db, booking, DateTimeOffset.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
        return r;
    });

    private Task<(PaymentStatus Status, decimal PayAmount, decimal AmountDue, decimal TotalFee)> State(Guid booking, Guid payment) => WithDb(async db =>
    {
        var p = await db.Payments.AsNoTracking().SingleAsync(x => x.PaymentId == payment);
        var b = await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == booking);
        return (p.Status, p.Amount, b.AmountDue, b.TotalFee);
    });

    [Fact]
    public async Task A_visit_the_doctor_waived_long_ago_but_that_is_still_unpaid_gets_waived()
    {
        var (booking, payment) = await LegacyVisit("Waive", "Friend", null, paymentAmount: 450, amountDue: 500, totalFee: 500); // Carla Reyes
        var r = await Run(booking);

        Assert.Equal(1, r.Waived);
        var s = await State(booking, payment);
        Assert.Equal(PaymentStatus.Waived, s.Status);
        Assert.Equal(0m, s.AmountDue);
    }

    [Fact]
    public async Task A_doctor_charge_amount_replaces_three_disagreeing_numbers()
    {
        var (booking, payment) = await LegacyVisit("Charge", null, 400m, paymentAmount: 450, amountDue: 350, totalFee: 350); // Q-1705
        var r = await Run(booking);

        Assert.Equal(1, r.AmountsAligned);
        var s = await State(booking, payment);
        Assert.Equal((PaymentStatus.Unpaid, 400m, 400m, 400m), s);
    }

    [Fact]
    public async Task Without_a_doctor_amount_the_payment_follows_the_amount_due()
    {
        var (booking, payment) = await LegacyVisit(null, null, null, paymentAmount: 450, amountDue: 500, totalFee: 500);
        await Run(booking);
        Assert.Equal(500m, (await State(booking, payment)).PayAmount);
    }

    [Fact]
    public async Task Running_it_twice_changes_nothing_the_second_time()
    {
        var (booking, _) = await LegacyVisit("Charge", null, 400m, paymentAmount: 450, amountDue: 350, totalFee: 350);
        Assert.True((await Run(booking)).Changed);
        Assert.False((await Run(booking)).Changed);
    }

    [Fact]
    public async Task Paid_visits_are_never_touched()
    {
        var (booking, payment) = await LegacyVisit("Waive", "Friend", null, paymentAmount: 450, amountDue: 0, totalFee: 450);
        await WithDb(async db => { (await db.Payments.SingleAsync(x => x.PaymentId == payment)).Status = PaymentStatus.Paid; await db.SaveChangesAsync(); return 0; });
        Assert.False((await Run(booking)).Changed);
        Assert.Equal(PaymentStatus.Paid, (await State(booking, payment)).Status);
    }

    [Fact]
    public async Task Staff_cannot_record_a_payment_for_a_visit_the_doctor_waived()
    {
        var (booking, payment) = await LegacyVisit("Waive", "Friend", null, paymentAmount: 450, amountDue: 500, totalFee: 500);
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.Conflict, await staff.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 500 })));

        var s = await State(booking, payment);
        Assert.Equal(PaymentStatus.Waived, s.Status); // and the stale record is repaired on the spot
        Assert.Equal(0m, s.AmountDue);
    }

    [Fact]
    public async Task Recording_a_payment_checks_against_the_doctors_amount_even_on_an_old_record()
    {
        var (_, payment) = await LegacyVisit("Charge", null, 400m, paymentAmount: 450, amountDue: 350, totalFee: 350);
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 399 })));
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 400 })));
    }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - staff SEE that a visit was waived (₱0 to collect) and confirm it, instead of the visit silently disappearing
[Collection("api")]
public class WaiverConfirmationTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<(Guid Booking, Guid Payment)> WaivedByDoctor()
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.InProgress;
            await db.SaveChangesAsync();
            return 0;
        });
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Completed", ["chief_complaint"] = "cough",
            ["visit_type"] = "New", ["pf_decision"] = "Waive", ["pf_waive_reason"] = "Friend of the doctor"
        })));
        await WithDb(async db =>
        {
            (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.Completed;
            await db.SaveChangesAsync();
            return 0;
        });
        return (booking, payment);
    }

    private async Task<List<System.Text.Json.JsonElement>> Pending()
    {
        using var staff = F.As(W.Staff, "Staff");
        return await ListOf(await staff.GetAsync("/api/payments/waived-awaiting-confirmation"));
    }

    [Fact]
    public async Task A_visit_the_doctor_waived_is_listed_for_the_front_desk_with_its_reason()
    {
        var (booking, _) = await WaivedByDoctor();
        var row = (await Pending()).Single(r => r.GetProperty("booking_id").GetGuid() == booking);
        Assert.Equal("Friend of the doctor", row.GetProperty("waived_reason").GetString());
    }

    [Fact]
    public async Task Confirming_the_waiver_removes_it_from_the_list_and_is_safe_to_repeat()
    {
        var (booking, payment) = await WaivedByDoctor();
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/confirm-waiver", null));
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/confirm-waiver", null)); // idempotent

        Assert.DoesNotContain(await Pending(), r => r.GetProperty("booking_id").GetGuid() == booking);
        var p = await WithDb(db => db.Payments.AsNoTracking().SingleAsync(x => x.PaymentId == payment));
        Assert.Equal(PaymentStatus.Waived, p.Status);   // still waived: ₱0 collected
        Assert.NotNull(p.ConfirmedAt);
    }

    [Fact]
    public async Task Only_a_waived_payment_can_be_confirmed_this_way()
    {
        var (_, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PostAsync($"/api/payments/{payment}/confirm-waiver", null));
    }

    [Fact]
    public async Task A_waiver_recorded_by_staff_at_the_desk_does_not_wait_for_confirmation()
    {
        // doctor decides Waive but the payment is waived by staff through the existing waive action
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Consultations.Add(new ClinicApp.Domain.Entities.Consultation
            {
                ConsultationId = Guid.NewGuid(), BookingId = booking, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId,
                Status = ConsultationStatus.Draft, PfDecision = "Waive", PfWaiveReason = "Friend", CreatedAt = now, UpdatedAt = now
            });
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/waive", ApiFactory.Body(new { reason = "Doctor decided" })));

        Assert.DoesNotContain(await Pending(), r => r.GetProperty("booking_id").GetGuid() == booking);
    }

    [Fact]
    public async Task Doctors_and_patients_cannot_confirm_a_waiver()
    {
        var (_, payment) = await WaivedByDoctor();
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBeDenied(await doc.PostAsync($"/api/payments/{payment}/confirm-waiver", null));
        using var patient = F.As(W.PatientA, "Patient");
        await ShouldBeDenied(await patient.PostAsync($"/api/payments/{payment}/confirm-waiver", null));
    }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - the Charge amount follows the computed total; an amend never silently contradicts a settled payment
[Collection("api")]
public class ChargeFollowsTotalAndAmendTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<(Guid Booking, Guid Payment)> NewVisit()
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        return (booking, payment);
    }

    private StringContent Save(string status, string? decision, decimal? amount = null, string? reason = null, bool medCert = false) =>
        ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = status, ["chief_complaint"] = "cough",
            ["visit_type"] = "New", ["med_cert_requested"] = medCert, ["pf_decision"] = decision, ["pf_amount"] = amount, ["pf_waive_reason"] = reason
        });

    private Task<(decimal Due, decimal PayAmount, PaymentStatus Status)> Money(Guid booking, Guid payment) => WithDb(async db =>
        ((await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == booking)).AmountDue,
         (await db.Payments.AsNoTracking().SingleAsync(x => x.PaymentId == payment)).Amount,
         (await db.Payments.AsNoTracking().SingleAsync(x => x.PaymentId == payment)).Status));

    private Task Put(HttpClient doc, Guid booking, HttpStatusCode expect, StringContent body) =>
        doc.PutAsync($"/api/consultations/by-booking/{booking}", body).ContinueWith(async t => await ShouldBe(expect, await t)).Unwrap();

    // ── the amount follows the computed total ────────────────────────────────

    [Fact]
    public async Task Without_a_typed_amount_a_medical_certificate_added_later_raises_what_is_collected()
    {
        var (booking, payment) = await NewVisit();
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.OK, Save("Draft", "Charge"));            // total at first save
        var first = await Money(booking, payment);
        await Put(doc, booking, HttpStatusCode.OK, Save("Draft", "Charge", medCert: true)); // the certificate is issued afterwards
        var second = await Money(booking, payment);

        Assert.True(second.Due > first.Due, "the certificate fee must be added");
        Assert.Equal(second.Due, second.PayAmount);   // payment and amount due agree
    }

    [Fact]
    public async Task A_deliberately_typed_amount_stays_even_when_the_total_changes()
    {
        var (booking, payment) = await NewVisit();
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.OK, Save("Draft", "Charge", amount: 300m));
        await Put(doc, booking, HttpStatusCode.OK, Save("Draft", "Charge", amount: 300m, medCert: true));

        var m = await Money(booking, payment);
        Assert.Equal(300m, m.Due);
        Assert.Equal(300m, m.PayAmount);
    }

    // ── amending after the payment is settled ────────────────────────────────

    private async Task<(Guid Booking, Guid Payment)> CompletedAs(string decision, decimal? amount, string? reason, PaymentStatus? forceStatus = null)
    {
        var (booking, payment) = await NewVisit();
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.OK, Save("Completed", decision, amount, reason));
        if (forceStatus is { } st)
            await WithDb(async db => { var p = await db.Payments.SingleAsync(x => x.PaymentId == payment); p.Status = st; await db.SaveChangesAsync(); return 0; });
        return (booking, payment);
    }

    [Fact]
    public async Task A_waived_visit_cannot_be_amended_into_a_charge()
    {
        var (booking, payment) = await CompletedAs("Waive", null, "Friend");
        Assert.Equal(PaymentStatus.Waived, (await Money(booking, payment)).Status);
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.Conflict, Save("Amended", "Charge", amount: 450m));
        Assert.Equal(PaymentStatus.Waived, (await Money(booking, payment)).Status);
    }

    [Fact]
    public async Task A_paid_visit_cannot_be_amended_into_a_waiver()
    {
        var (booking, _) = await CompletedAs("Charge", 450m, null, PaymentStatus.Paid);
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.Conflict, Save("Amended", "Waive", reason: "Friend"));
    }

    [Fact]
    public async Task A_paid_amount_cannot_be_restated_but_the_same_or_no_amount_is_fine()
    {
        var (booking, _) = await CompletedAs("Charge", 450m, null, PaymentStatus.Paid);
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.Conflict, Save("Amended", "Charge", amount: 300m));
        await Put(doc, booking, HttpStatusCode.OK, Save("Amended", "Charge", amount: 450m));
        await Put(doc, booking, HttpStatusCode.OK, Save("Amended", "Charge"));
    }

    [Fact]
    public async Task Re_amending_with_the_same_waive_decision_is_fine()
    {
        var (booking, _) = await CompletedAs("Waive", null, "Friend");
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.OK, Save("Amended", "Waive", reason: "Friend"));
    }

    [Fact]
    public async Task A_visit_with_no_stored_decision_still_amends_normally_even_if_its_payment_is_settled()
    {
        var (booking, payment) = await NewVisit();
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Consultations.Add(new ClinicApp.Domain.Entities.Consultation
            {
                ConsultationId = Guid.NewGuid(), BookingId = booking, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId,
                Status = ConsultationStatus.Completed, ChiefComplaint = "cough", PfDecision = null, CompletedAt = now, CreatedAt = now, UpdatedAt = now
            });
            (await db.Payments.SingleAsync(x => x.PaymentId == payment)).Status = PaymentStatus.Waived;
            await db.SaveChangesAsync();
            return 0;
        });
        using var doc = F.As(W.DoctorA, "Doctor");
        await Put(doc, booking, HttpStatusCode.OK, Save("Amended", "Charge"));   // nothing was reversed: an older visit can still be corrected
    }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - review follow-ups: cancel of a paid booking, old unconfirmed waivers stay listed, made-up discount on the doctor's save, rescheduled bookings are not "unpaid"
[Collection("api")]
public class ReviewFollowUpTests(ApiFixture api) : ApiTestBase(api)
{
    [Fact]
    public async Task The_status_endpoint_refuses_to_cancel_a_paid_booking()
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.Confirmed;
            (await db.Payments.SingleAsync(x => x.PaymentId == payment)).Status = PaymentStatus.Paid;
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.Conflict, await staff.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = "Cancelled", reason = "left" })));
        Assert.Equal(BookingStatus.Confirmed, await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));
    }

    [Fact]
    public async Task An_unpaid_booking_can_still_be_cancelled_through_the_status_endpoint()
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.Confirmed; await db.SaveChangesAsync(); return 0; });
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = "Cancelled", reason = "left" })));
    }

    [Fact]
    public async Task A_waived_visit_nobody_confirmed_stays_on_the_list_however_old_it_is()
    {
        var (booking, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            var b = await db.Bookings.SingleAsync(x => x.BookingId == booking);
            b.Status = BookingStatus.Completed; b.AppointmentDate = ClinicApp.Domain.ClinicClock.Today.AddDays(-30); b.AmountDue = 0;
            var p = await db.Payments.SingleAsync(x => x.PaymentId == payment);
            p.Status = PaymentStatus.Waived; p.WaivedReason = "Friend"; p.ConfirmedAt = null;
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");
        var rows = await ListOf(await staff.GetAsync("/api/payments/waived-awaiting-confirmation"));
        Assert.Contains(rows, r => r.GetProperty("booking_id").GetGuid() == booking);
    }

    [Fact]
    public async Task The_doctors_save_refuses_a_made_up_discount()
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(x => x.BookingId == booking)).Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Draft", ["chief_complaint"] = "cough",
            ["visit_type"] = "New", ["discount_category"] = "x", ["pf_decision"] = "Charge"
        })));
    }
}
//JJQR - END
