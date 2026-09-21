using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

/// <summary>P0.5 — Patient A tries to read or change Patient B's data by id, by filter, and by
/// leaving the filter off. Every case must be denied (or return only A's own rows). Each block
/// also has a positive control so a blanket "everything is 403" can't make these pass.</summary>
[Collection("api")]
public class IdorTests(ApiFixture api) : ApiTestBase(api)
{
    // ── bookings ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Patient_cannot_read_another_patients_booking_by_id()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.GetAsync($"/api/bookings/{W.BookingB}"));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/bookings/{W.BookingA}")); // control
    }

    [Fact]
    public async Task Patient_booking_list_is_scoped_to_self()
    {
        using var a = F.Patient(W.PatientA);
        var own = await ListOf(await a.GetAsync("/api/bookings"));
        Assert.True(AnyValue(own, "booking_id", W.BookingA));
        Assert.False(AnyValue(own, "booking_id", W.BookingB));

        await ShouldBeDenied(await a.GetAsync($"/api/bookings?patientId={W.PatientB.RowId}"));
        var mine = await ListOf(await a.GetAsync("/api/bookings/me"));
        Assert.All(mine, r => Assert.Equal(W.PatientA.RowId, r.GetProperty("patient_id").GetGuid()));
    }

    [Fact]
    public async Task Patient_cannot_cancel_or_change_status_of_anothers_booking()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.PutAsync($"/api/bookings/{W.BookingB}/cancel", ApiFactory.Body(new { reason = "x" })));
        await ShouldBeDenied(await a.PutAsync($"/api/bookings/{W.BookingA}/status", ApiFactory.Body(new { status = "Completed" }))); // staff-only endpoint
        Assert.NotEqual(BookingStatus.Cancelled, await WithDb(db => db.Bookings.Where(b => b.BookingId == W.BookingB).Select(b => b.Status).SingleAsync()));
    }

    // ── patients ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Patient_cannot_read_or_edit_another_patient_record()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.GetAsync($"/api/patients/{W.PatientB.RowId}"));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/patients/{W.PatientA.RowId}")); // control

        var victim = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == W.PatientB.RowId));
        var res = await a.PutAsync($"/api/patients/{W.PatientB.RowId}", ApiFactory.Body(new { first_name = "PWNED", last_name = "PWNED", date_of_birth = "1990-01-01", sex = "Male" }));
        await ShouldBeDenied(res);
        await ShouldBeDenied(await a.PutAsync($"/api/patients/{W.PatientB.RowId}/consent", ApiFactory.Body(99)));
        var after = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == W.PatientB.RowId));
        Assert.Equal(victim.FirstName, after.FirstName);
        Assert.Equal(victim.ConsentVersion, after.ConsentVersion);
    }

    [Fact]
    public async Task Patient_can_edit_own_record()
    {
        using var a = F.Patient(W.PatientA);
        var res = await a.PutAsync($"/api/patients/{W.PatientA.RowId}", ApiFactory.Body(new { first_name = "Alice", last_name = "Tester", date_of_birth = "1990-01-01", sex = "Female", city = "Cebu" }));
        await ShouldBe(HttpStatusCode.OK, res);
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/patients/search")]
    [InlineData("/api/staff-accounts")]
    [InlineData("/api/audit-logs")]
    [InlineData("/api/audit-logs/search")]
    [InlineData("/api/queue")]
    public async Task Patient_cannot_use_staff_only_lists(string url)
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync(url));
    }

    [Fact]
    public async Task Patient_cannot_list_another_patients_vaccinations()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync($"/api/patients/{W.PatientB.RowId}/vaccinations"));
        await ShouldBeDenied(await a.GetAsync($"/api/patient-vaccinations?patientId={W.PatientB.RowId}"));
        var own = await ListOf(await a.GetAsync("/api/patient-vaccinations"));
        Assert.NotEmpty(own);
        Assert.All(own, r => Assert.Equal(W.PatientA.RowId, r.GetProperty("patient_id").GetGuid()));
        var byConsult = await ListOf(await a.GetAsync($"/api/patient-vaccinations/by-consultation/{W.ConsultationB}"));
        Assert.Empty(byConsult);
    }

    // ── payments ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Patient_cannot_read_another_patients_payment()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.GetAsync($"/api/payments/{W.PaymentB}"));
        await ShouldBeDenied(await a.GetAsync($"/api/payments/booking/{W.BookingB}"));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/payments/{W.PaymentA}")); // control
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/payments/booking/{W.BookingA}"));
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("waive")]
    [InlineData("refund")]
    public async Task Patient_cannot_mutate_any_payment_even_their_own(string action)
    {
        using var a = F.Patient(W.PatientA);
        var body = ApiFactory.Body(new { payment_method = "Cash", amount_received = 1, reason = "x", amount = 1 });
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync($"/api/payments/{W.PaymentA}/{action}", body));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync($"/api/payments/{W.PaymentB}/{action}", body));
        Assert.Equal(PaymentStatus.Unpaid, await WithDb(db => db.Payments.Where(p => p.PaymentId == W.PaymentA).Select(p => p.Status).SingleAsync()));
    }

    // ── consultations & clinical records ─────────────────────────────────
    [Fact]
    public async Task Patient_cannot_read_another_patients_consultation()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.GetAsync($"/api/consultations/{W.ConsultationB}"));
        await ShouldBeDenied(await a.GetAsync($"/api/consultations/by-booking/{W.BookingB}"));
        await ShouldBeDenied(await a.GetAsync($"/api/consultations?patientId={W.PatientB.RowId}"));
        await ShouldBeDenied(await a.GetAsync($"/api/consultations/{W.ConsultationB}/diagnoses"));

        var own = await ListOf(await a.GetAsync("/api/consultations")); // unfiltered → only mine
        Assert.True(AnyValue(own, "consultation_id", W.ConsultationA));
        Assert.False(AnyValue(own, "consultation_id", W.ConsultationB));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/consultations/{W.ConsultationA}")); // control
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/consultations/{W.ConsultationA}/diagnoses"));
    }

    [Fact]
    public async Task Patient_cannot_write_consultation_data()
    {
        using var a = F.Patient(W.PatientA);
        var req = ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, status = "Draft", chief_complaint = "tampered" });
        await ShouldBe(HttpStatusCode.Forbidden, await a.PutAsync($"/api/consultations/by-booking/{W.BookingA}", req));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PutAsync($"/api/consultations/{W.ConsultationA}/diagnoses", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PutAsync($"/api/lab-orders/by-consultation/{W.ConsultationA}", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PutAsync($"/api/prescription-groups/by-booking/{W.BookingA}", ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, booking_id = W.BookingA, items = new object[0] })));
    }

    [Fact]
    public async Task Patient_cannot_read_another_patients_prescriptions()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.GetAsync($"/api/prescription-groups/{W.RxGroupB}"));
        await ShouldBeDenied(await a.GetAsync($"/api/prescription-groups?patientId={W.PatientB.RowId}"));
        var own = await ListOf(await a.GetAsync("/api/prescription-groups"));
        Assert.True(AnyValue(own, "group_id", W.RxGroupA));
        Assert.False(AnyValue(own, "group_id", W.RxGroupB));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/prescription-groups/{W.RxGroupA}"));
    }

    [Fact]
    public async Task Patient_cannot_read_another_patients_lab_orders_follow_ups_certificates()
    {
        using var a = F.Patient(W.PatientA);
        foreach (var path in new[] { "lab-orders", "follow-ups", "medical-certificates" })
        {
            await ShouldBeDenied(await a.GetAsync($"/api/{path}?patientId={W.PatientB.RowId}"));
            var own = await ListOf(await a.GetAsync($"/api/{path}"));
            Assert.NotEmpty(own);
            Assert.All(own, r => Assert.Equal(W.PatientA.RowId, r.GetProperty("patient_id").GetGuid()));
        }
        Assert.Empty(await ListOf(await a.GetAsync($"/api/lab-orders/by-consultation/{W.ConsultationB}")));
        Assert.Empty(await ListOf(await a.GetAsync($"/api/lab-orders/by-booking/{W.BookingB}")));
        await ShouldBeDenied(await a.GetAsync($"/api/follow-ups/by-consultation/{W.ConsultationB}"));
        await ShouldBeDenied(await a.GetAsync($"/api/medical-certificates/by-consultation/{W.ConsultationB}"));
        await ShouldBeDenied(await a.GetAsync($"/api/medical-certificates/by-booking/{W.BookingB}"));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/medical-certificates/by-booking/{W.BookingA}")); // control
    }

    [Fact]
    public async Task Patient_cannot_read_another_patients_vitals()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.GetAsync($"/api/vitals?patientId={W.PatientB.RowId}"));
        Assert.All(await ListOf(await a.GetAsync("/api/vitals")), r => Assert.Equal(W.PatientA.RowId, r.GetProperty("patient_id").GetGuid()));
    }

    // ── reviews ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Patient_cannot_read_another_patients_review_by_booking_or_patient()
    {
        using var a = F.Patient(W.PatientA);
        Assert.Empty(await ListOf(await a.GetAsync($"/api/reviews?bookingId={W.BookingB}")));
        await ShouldBeDenied(await a.GetAsync($"/api/reviews?patientId={W.PatientB.RowId}"));
        Assert.False(AnyValue(await ListOf(await a.GetAsync("/api/reviews")), "review_id", W.ReviewB));

        // A doctor's public rating list is allowed (RLS reviews_select_authenticated).
        Assert.True(AnyValue(await ListOf(await a.GetAsync($"/api/reviews?doctorId={W.DoctorA.RowId}")), "review_id", W.ReviewB));
    }

    [Fact]
    public async Task Patient_cannot_review_a_booking_that_is_not_theirs()
    {
        var (bookingId, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientB.RowId, W.DoctorA.RowId));
        using var a = F.Patient(W.PatientA);
        await ShouldBeDenied(await a.PostAsync("/api/reviews", ApiFactory.Body(new { booking_id = bookingId, doctor_id = W.DoctorA.RowId, patient_id = W.PatientB.RowId, rating = 1 })));
        await ShouldBeDenied(await a.PostAsync("/api/reviews", ApiFactory.Body(new { booking_id = bookingId, doctor_id = W.DoctorA.RowId, patient_id = W.PatientA.RowId, rating = 1 })));
        Assert.False(await WithDb(db => db.Reviews.AnyAsync(r => r.BookingId == bookingId)));
    }

    [Fact]
    public async Task Review_identity_comes_from_the_booking_not_the_body()
    {
        var (bookingId, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        using var a = F.Patient(W.PatientA);
        // Spoofed patient_id / doctor_id in the body are ignored.
        var res = await a.PostAsync("/api/reviews", ApiFactory.Body(new { booking_id = bookingId, doctor_id = W.DoctorB.RowId, patient_id = W.PatientB.RowId, rating = 4, comment = "ok" }));
        await ShouldBe(HttpStatusCode.OK, res);
        var row = await WithDb(db => db.Reviews.AsNoTracking().SingleAsync(r => r.BookingId == bookingId));
        Assert.Equal(W.PatientA.RowId, row.PatientId);
        Assert.Equal(W.DoctorA.RowId, row.DoctorId);
    }

    [Fact]
    public async Task Staff_roles_cannot_post_reviews()
    {
        using var s = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.Forbidden, await s.PostAsync("/api/reviews", ApiFactory.Body(new { booking_id = W.BookingA, doctor_id = W.DoctorA.RowId, patient_id = W.PatientA.RowId, rating = 5 })));
    }

    // ── staff directory ──────────────────────────────────────────────────
    [Fact]
    public async Task Patient_can_only_read_active_doctor_staff_rows_or_their_own()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/staff-accounts/{W.DoctorA.RowId}"));   // public doctor catalog
        await ShouldBeDenied(await a.GetAsync($"/api/staff-accounts/{W.Admin.RowId}"));                  // admin row is not
        await ShouldBeDenied(await a.GetAsync($"/api/staff-accounts/{W.Staff.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PutAsync($"/api/staff-accounts/{W.DoctorA.RowId}", ApiFactory.Body(new { full_name = "pwned" })));
    }

    // ── doctor-private tooling ───────────────────────────────────────────
    [Fact]
    public async Task Patient_cannot_read_doctor_templates_or_favorites()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync("/api/prescription-templates"));
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync($"/api/doctor-favorite-medicines?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync("/api/medical-certificate-templates"));
    }

    // ── anonymous ────────────────────────────────────────────────────────
    [Theory]
    [InlineData("/api/bookings")]
    [InlineData("/api/patients/me")]
    [InlineData("/api/consultations")]
    [InlineData("/api/payments/booking/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/patient-documents")]
    [InlineData("/api/patient-lab-results")]
    [InlineData("/api/reviews")]
    [InlineData("/api/prescription-groups")]
    [InlineData("/api/audit-logs")]
    public async Task Anonymous_callers_get_401(string url)
    {
        using var anon = F.Anonymous();
        await ShouldBe(HttpStatusCode.Unauthorized, await anon.GetAsync(url));
    }

    [Fact]
    public async Task Forged_token_is_rejected()
    {
        using var c = F.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ4Iiwicm9sZSI6IkFkbWluIn0.AAAA");
        await ShouldBe(HttpStatusCode.Unauthorized, await c.GetAsync("/api/patients"));
    }
}
