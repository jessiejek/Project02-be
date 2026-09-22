using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

/// <summary>Role gates the RLS policies encoded (waive = doctor's call, refund = Admin, audit = Admin,
/// staff directory writes = own row or Admin) plus the doctor-vs-doctor boundaries RLS didn't have but
/// a multi-doctor clinic needs. Positive controls throughout.</summary>
[Collection("api")]
public class RoleTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient Admin() => F.As(W.Admin, "Admin");
    private HttpClient StaffC() => F.As(W.Staff, "Staff");
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");
    private HttpClient DocB() => F.As(W.DoctorB, "Doctor");

    private async Task<(Guid Booking, Guid Payment)> FreshBooking() =>
        await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));

    private async Task<PaymentStatus> PaymentStatusOf(Guid paymentId) =>
        await WithDb(db => db.Payments.AsNoTracking().Where(p => p.PaymentId == paymentId).Select(p => p.Status).SingleAsync());

    // ── payments ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Staff")]
    [InlineData("Doctor")]
    [InlineData("Patient")]
    public async Task Only_admin_can_refund(string role)
    {
        var (_, payment) = await FreshBooking();
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 450 })));
        Assert.Equal(PaymentStatus.Paid, await PaymentStatusOf(payment));

        using var c = role switch { "Staff" => StaffC(), "Doctor" => DocA(), _ => F.Patient(W.PatientA) };
        await ShouldBe(HttpStatusCode.Forbidden, await c.PostAsync($"/api/payments/{payment}/refund", ApiFactory.Body(new { amount = 100, reason = "x" })));
        Assert.Equal(PaymentStatus.Paid, await PaymentStatusOf(payment));

        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/payments/{payment}/refund", ApiFactory.Body(new { amount = 100, reason = "ok" })));
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusOf(payment));
    }

    [Fact]
    public async Task Staff_cannot_waive_on_their_own_but_can_record_a_waiver_the_doctor_decided()
    {
        var (booking, payment) = await FreshBooking();
        using var staff = StaffC();
        var body = ApiFactory.Body(new { reason = "indigent" });

        var denied = await staff.PostAsync($"/api/payments/{payment}/waive", body);
        await ShouldBe(HttpStatusCode.Forbidden, denied);
        Assert.Equal(PaymentStatus.Unpaid, await PaymentStatusOf(payment));

        // The doctor decides "Waive" on the consultation…
        await WithDb(async db =>
        {
            db.Consultations.Add(new ClinicApp.Domain.Entities.Consultation
            {
                ConsultationId = Guid.NewGuid(), BookingId = booking, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId,
                Status = ConsultationStatus.Completed, PfDecision = "Waive", PfWaiveReason = "indigent",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return 0;
        });
        // …and only then may the front desk record it.
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{payment}/waive", body));
        Assert.Equal(PaymentStatus.Waived, await PaymentStatusOf(payment));
    }

    [Fact]
    public async Task Doctor_can_waive_their_own_bookings_but_not_another_doctors()
    {
        var (_, own) = await FreshBooking(); // booking belongs to DoctorA
        using var docB = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await docB.PostAsync($"/api/payments/{own}/waive", ApiFactory.Body(new { reason = "x" })));
        Assert.Equal(PaymentStatus.Unpaid, await PaymentStatusOf(own));

        using var docA = DocA();
        await ShouldBe(HttpStatusCode.OK, await docA.PostAsync($"/api/payments/{own}/waive", ApiFactory.Body(new { reason = "charity" })));
        Assert.Equal(PaymentStatus.Waived, await PaymentStatusOf(own));
    }

    [Fact]
    public async Task Admin_can_waive_and_only_admin_or_staff_can_confirm()
    {
        var (_, p1) = await FreshBooking();
        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/payments/{p1}/waive", ApiFactory.Body(new { reason = "x" })));

        var (_, p2) = await FreshBooking();
        var confirm = ApiFactory.Body(new { payment_method = "Cash", amount_received = 450 });
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.Forbidden, await doc.PostAsync($"/api/payments/{p2}/confirm", confirm));
        Assert.Equal(PaymentStatus.Unpaid, await PaymentStatusOf(p2));
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{p2}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 450 })));
        Assert.Equal(PaymentStatus.Paid, await PaymentStatusOf(p2));
    }

    // ── bookings ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Patient_cannot_use_the_staff_booking_endpoints()
    {
        using var a = F.Patient(W.PatientA);
        var body = ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, appointment_date = "2030-01-01", slot_start_time = "09:00", slot_end_time = "09:15", service_ids = new Guid[0], payment_mode = "PayAtClinic", is_walk_in = false });
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync("/api/bookings", body));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync("/api/bookings/walk-in", body));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync("/api/queue", ApiFactory.Body(new { patient_id = W.PatientA.RowId })));
        await ShouldBe(HttpStatusCode.Forbidden, await a.GetAsync("/api/bookings/staff/all"));
    }

    [Fact]
    public async Task Doctor_can_only_change_status_of_their_own_bookings()
    {
        var (booking, _) = await FreshBooking();
        // Fresh fixtures start Completed (terminal); put the row into a state that can become NoShow.
        await WithDb(async db =>
        {
            var b = await db.Bookings.SingleAsync(x => x.BookingId == booking);
            b.Status = BookingStatus.CheckedIn;
            await db.SaveChangesAsync();
            return 0;
        });

        using var docB = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await docB.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = "NoShow" })));
        await ShouldBe(HttpStatusCode.Forbidden, await docB.PutAsync($"/api/queue/{booking}/no-show", null));
        Assert.Equal(BookingStatus.CheckedIn, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));

        using var docA = DocA();
        await ShouldBe(HttpStatusCode.OK, await docA.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = "NoShow" })));
        Assert.Equal(BookingStatus.NoShow, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));
    }

    // ── audit logs ───────────────────────────────────────────────────────
    [Fact]
    public async Task Audit_trail_is_admin_only_except_a_doctors_consultation_history()
    {
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.Forbidden, await staff.GetAsync("/api/audit-logs"));
        await ShouldBe(HttpStatusCode.Forbidden, await staff.GetAsync("/api/audit-logs?entityType=Consultation"));

        using var doc = DocA();
        await ShouldBe(HttpStatusCode.Forbidden, await doc.GetAsync("/api/audit-logs"));                       // unscoped: would include patient edits
        await ShouldBe(HttpStatusCode.Forbidden, await doc.GetAsync("/api/audit-logs?entityType=Patient"));
        await ShouldBe(HttpStatusCode.Forbidden, await doc.GetAsync("/api/audit-logs/search"));
        await ShouldBe(HttpStatusCode.OK, await doc.GetAsync($"/api/audit-logs?entityType=Consultation&entityId={W.ConsultationA}")); // amend history

        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.GetAsync("/api/audit-logs"));
        await ShouldBe(HttpStatusCode.OK, await admin.GetAsync("/api/audit-logs/search"));
    }

    // ── staff directory ──────────────────────────────────────────────────
    [Fact]
    public async Task Staff_directory_writes_are_own_row_or_admin()
    {
        var body = ApiFactory.Body(new { full_name = "Renamed", status = "Active" });
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PutAsync($"/api/staff-accounts/{W.DoctorA.RowId}", body)); // not their row
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/staff-accounts/{W.Staff.RowId}", body));           // own row

        using var docB = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await docB.PutAsync($"/api/staff-accounts/{W.DoctorA.RowId}", body));

        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.PutAsync($"/api/staff-accounts/{W.DoctorA.RowId}", body));
    }

    [Fact]
    public async Task Staff_cannot_deactivate_someone_via_a_self_edit_or_change_own_status()
    {
        using var staff = StaffC();
        await staff.PutAsync($"/api/staff-accounts/{W.Staff.RowId}", ApiFactory.Body(new { full_name = "Me", status = "Inactive" }));
        Assert.Equal(StaffStatus.Active, await WithDb(db => db.StaffAccounts.Where(s => s.StaffId == W.Staff.RowId).Select(s => s.Status).SingleAsync()));
    }

    // ── doctor vs doctor ─────────────────────────────────────────────────
    [Fact]
    public async Task Doctor_cannot_write_another_doctors_clinical_records()
    {
        using var b = DocB();
        var consult = ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, status = "Draft", chief_complaint = "tampered" });
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/consultations/by-booking/{W.BookingA}", consult));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/consultations/{W.ConsultationA}/diagnoses", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/lab-orders/by-consultation/{W.ConsultationA}", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/patient-vaccinations/by-consultation/{W.ConsultationA}", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/follow-ups/by-consultation/{W.ConsultationA}", ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, follow_up_date = "2030-01-01", reminder_enabled = false, status = "Pending" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/follow-ups/by-consultation/{W.ConsultationA}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/medical-certificates/by-consultation/{W.ConsultationA}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/medical-certificates/by-consultation/{W.ConsultationA}", ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/prescription-groups/by-booking/{W.BookingA}", ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, booking_id = W.BookingA, items = new object[0] })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/prescription-groups/{W.RxGroupA}"));

        Assert.Equal("cough", await WithDb(db => db.Consultations.Where(c => c.ConsultationId == W.ConsultationA).Select(c => c.ChiefComplaint).SingleAsync()));
        Assert.True(await WithDb(db => db.PrescriptionGroups.AnyAsync(g => g.GroupId == W.RxGroupA)));
    }

    [Fact]
    public async Task Owning_doctor_and_admin_can_write_the_record()
    {
        var ok = ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, status = "Draft", chief_complaint = "still coughing" });
        using var a = DocA();
        await ShouldBe(HttpStatusCode.OK, await a.PutAsync($"/api/consultations/by-booking/{W.BookingA}", ok));
        await ShouldBe(HttpStatusCode.OK, await a.PutAsync($"/api/consultations/{W.ConsultationA}/diagnoses", ApiFactory.Body(new[] { new { custom_description = "cold", type = "Primary" } })));
        await ShouldBe(HttpStatusCode.OK, await a.PutAsync($"/api/lab-orders/by-consultation/{W.ConsultationA}", ApiFactory.Body(new[] { new { test_name = "CBC" } })));
        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.PutAsync($"/api/consultations/by-booking/{W.BookingA}", ok));
    }

    [Fact]
    public async Task Consultation_identity_must_match_the_booking()
    {
        using var a = DocA();
        // Someone else's patient id on this booking.
        await ShouldBe(HttpStatusCode.BadRequest, await a.PutAsync($"/api/consultations/by-booking/{W.BookingA}",
            ApiFactory.Body(new { patient_id = W.PatientB.RowId, doctor_id = W.DoctorA.RowId, status = "Draft" })));
        // Wrong doctor id.
        await ShouldBe(HttpStatusCode.BadRequest, await a.PutAsync($"/api/consultations/by-booking/{W.BookingA}",
            ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorB.RowId, status = "Draft" })));
        // Nonexistent booking.
        await ShouldBe(HttpStatusCode.NotFound, await a.PutAsync($"/api/consultations/by-booking/{Guid.NewGuid()}",
            ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, status = "Draft" })));
    }

    [Fact]
    public async Task Staff_cannot_write_clinical_records()
    {
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PutAsync($"/api/consultations/by-booking/{W.BookingA}", ApiFactory.Body(new { patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, status = "Draft" })));
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PutAsync($"/api/consultations/{W.ConsultationA}/diagnoses", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PutAsync($"/api/lab-orders/by-consultation/{W.ConsultationA}", ApiFactory.Body(new object[0])));
        await ShouldBe(HttpStatusCode.Forbidden, await staff.DeleteAsync($"/api/prescription-groups/{W.RxGroupA}"));
    }

    // ── doctor-private tooling ───────────────────────────────────────────
    [Fact]
    public async Task Doctors_cannot_read_or_change_each_others_private_templates_and_favorites()
    {
        using var b = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/doctor-favorite-medicines?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/doctor-favorite-medicines/{W.FavoriteDoctorA}", ApiFactory.Body(new { medicine_id = Guid.NewGuid(), generic_name = "x", dosage = "x", quantity = "1" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/doctor-favorite-medicines/{W.FavoriteDoctorA}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PostAsync($"/api/doctor-favorite-medicines?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { medicine_id = Guid.NewGuid(), generic_name = "x", dosage = "x", quantity = "1" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/soap-phrases?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/soap-templates/{W.SoapTemplateDoctorA}", ApiFactory.Body(new { title = "hijacked", is_system_template = false })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/soap-templates/{W.SoapTemplateDoctorA}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PostAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { title = "planted", is_system_template = false })));

        // Unfiltered list still doesn't leak DoctorA's rows to DoctorB.
        Assert.False(AnyValue(await ListOf(await b.GetAsync("/api/soap-templates")), "id", W.SoapTemplateDoctorA));
        Assert.True(await WithDb(db => db.SoapTemplates.AnyAsync(t => t.Id == W.SoapTemplateDoctorA && t.Title == "DoctorA private")));

        using var a = DocA(); // control
        Assert.True(AnyValue(await ListOf(await a.GetAsync("/api/soap-templates")), "id", W.SoapTemplateDoctorA));
        await ShouldBe(HttpStatusCode.OK, await a.GetAsync($"/api/doctor-favorite-medicines?doctorId={W.DoctorA.RowId}"));
    }

    [Fact]
    public async Task Only_admin_can_create_system_templates()
    {
        using var a = DocA();
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { title = "sys", is_system_template = true })));
        await ShouldBe(HttpStatusCode.OK, await a.PostAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { title = "mine", is_system_template = false })));
        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { title = "sys", is_system_template = true })));
    }

    // ── doctor schedule tooling ──────────────────────────────────────────
    [Fact]
    public async Task Doctor_cannot_edit_another_doctors_schedule_or_blocked_dates()
    {
        using var b = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/doctors/{W.DoctorA.RowId}/schedules", ApiFactory.Body(new { day_of_week = 1, is_active = false, start_time = "08:00", end_time = "17:00" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PostAsync($"/api/doctors/{W.DoctorA.RowId}/blocked-dates", ApiFactory.Body(new { blocked_date = "2030-01-01" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/doctors/{W.DoctorA.RowId}/day-status", ApiFactory.Body(new { status_date = "2030-01-01", status = "UnavailableToday" })));

        using var a = DocA();
        await ShouldBe(HttpStatusCode.OK, await a.PutAsync($"/api/doctors/{W.DoctorA.RowId}/day-status", ApiFactory.Body(new { status_date = "2030-01-01", status = "Available" })));
        using var staff = StaffC(); // front desk sets any doctor's day status
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/doctors/{W.DoctorA.RowId}/day-status", ApiFactory.Body(new { status_date = "2030-01-02", status = "RunningLate", running_late_minutes = 15 })));
    }

    [Fact]
    public async Task Non_admins_cannot_edit_doctor_profiles_or_settings()
    {
        var doc = ApiFactory.Body(new { specialization = "x", consultation_fee = 1 });
        using var a = DocA();
        await ShouldBe(HttpStatusCode.Forbidden, await a.PutAsync($"/api/doctors/{W.DoctorA.RowId}", doc));
        using var p = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.Forbidden, await p.PutAsync($"/api/doctors/{W.DoctorA.RowId}", doc));
        await ShouldBe(HttpStatusCode.Forbidden, await p.PutAsync("/api/settings", ApiFactory.Body(new { clinic_name = "pwned" })));
        await ShouldBe(HttpStatusCode.Forbidden, await p.PostAsync("/api/announcements", ApiFactory.Body(new { title = "x", body = "x" })));
    }

    // ── read access for clinic staff (positive controls) ─────────────────
    [Theory]
    [InlineData("Admin")]
    [InlineData("Staff")]
    [InlineData("Doctor")]
    public async Task Clinic_staff_can_read_any_patients_records(string role)
    {
        using var c = F.ClientFor(role switch { "Admin" => W.Admin.UserId, "Staff" => W.Staff.UserId, _ => W.DoctorA.UserId }, role);
        await ShouldBe(HttpStatusCode.OK, await c.GetAsync($"/api/patients/{W.PatientB.RowId}"));
        await ShouldBe(HttpStatusCode.OK, await c.GetAsync($"/api/bookings/{W.BookingB}"));
        await ShouldBe(HttpStatusCode.OK, await c.GetAsync($"/api/payments/booking/{W.BookingB}"));
        await ShouldBe(HttpStatusCode.OK, await c.GetAsync($"/api/consultations/{W.ConsultationB}"));
        await ShouldBe(HttpStatusCode.OK, await c.GetAsync($"/api/prescription-groups/{W.RxGroupB}"));
        await ShouldBe(HttpStatusCode.OK, await c.GetAsync($"/api/patient-documents?patientId={W.PatientB.RowId}"));
    }
}
