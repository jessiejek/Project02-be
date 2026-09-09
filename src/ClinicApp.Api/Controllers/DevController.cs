using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>
/// Development-only. Mirrors rows from the Supabase project into this DB with
/// their original primary keys so the parity harness (Project02-fe
/// scripts/parity) compares like-for-like during the migration.
/// Returns 404 outside the Development environment.
///
/// POST /api/dev/import   { "table": "doctors", "rows": [ { ...snake_case row... } ] }
/// Upserts by PK. Call in FK order (services, staff_accounts, patients, doctors,
/// then the doctor_* children). See scripts/dev/import-from-supabase.mjs.
/// </summary>
[ApiController]
[Route("api/dev")]
public sealed class DevController(ClinicAppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    public sealed record ImportRequest(string Table, List<JsonElement> Rows);

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportRequest req, CancellationToken ct)
    {
        if (!env.IsDevelopment()) return NotFound();

        int upserted;
        try
        {
            upserted = req.Table switch
            {
                "medicines"             => await UpsertMedicines(req.Rows, ct),
                "vital_field_templates" => await UpsertVitalTemplates(req.Rows, ct),
                "services"              => await UpsertServices(req.Rows, ct),
                "staff_accounts"        => await UpsertStaffAccounts(req.Rows, ct),
                "patients"              => await UpsertPatients(req.Rows, ct),
                "doctors"               => await UpsertDoctors(req.Rows, ct),
                "doctor_services"       => await UpsertDoctorServices(req.Rows, ct),
                "doctor_schedules"      => await UpsertDoctorSchedules(req.Rows, ct),
                "doctor_blocked_dates"  => await UpsertBlockedDates(req.Rows, ct),
                "doctor_day_statuses"   => await UpsertDayStatuses(req.Rows, ct),
                "bookings"              => await UpsertBookings(req.Rows, ct),
                "booking_services"      => await UpsertBookingServices(req.Rows, ct),
                "payments"              => await UpsertPayments(req.Rows, ct),
                "consultations"         => await UpsertConsultations(req.Rows, ct),
                "consultation_diagnoses"=> await UpsertConsultationDiagnoses(req.Rows, ct),
                "patient_vital_readings"=> await UpsertVitalReadings(req.Rows, ct),
                "follow_ups"            => await UpsertFollowUps(req.Rows, ct),
                "prescription_groups"   => await UpsertRxGroups(req.Rows, ct),
                "prescription_line_items" => await UpsertRxLineItems(req.Rows, ct),
                "prescription_templates" => await UpsertRxTemplates(req.Rows, ct),
                "prescription_template_items" => await UpsertRxTemplateItems(req.Rows, ct),
                "doctor_favorite_medicines" => await UpsertFavMedicines(req.Rows, ct),
                "soap_templates"        => await UpsertSoapTemplates(req.Rows, ct),
                "soap_phrases"          => await UpsertSoapPhrases(req.Rows, ct),
                "reviews"               => await UpsertReviews(req.Rows, ct),
                "patient_vaccinations"  => await UpsertVaccinations(req.Rows, ct),
                "patient_documents"     => await UpsertDocuments(req.Rows, ct),
                "patient_lab_results"   => await UpsertLabResults(req.Rows, ct),
                "announcements"         => await UpsertAnnouncements(req.Rows, ct),
                "audit_logs"            => await UpsertAuditLogs(req.Rows, ct),
                _ => -1,
            };
        }
        catch (Exception ex)
        {
            return BadRequest(new { table = req.Table, error = ex.Message });
        }

        if (upserted < 0) return BadRequest(new { error = $"Unsupported table '{req.Table}'." });
        return Ok(new { table = req.Table, upserted });
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private static Guid G(JsonElement r, string k) => Guid.Parse(r.GetProperty(k).GetString()!);
    private static Guid? GN(JsonElement r, string k) =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? Guid.Parse(v.GetString()!) : null;
    private static string S(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static string? SN(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool BN(JsonElement r, string k, bool dflt) => r.TryGetProperty(k, out var v) && (v.ValueKind is JsonValueKind.True or JsonValueKind.False) ? v.GetBoolean() : dflt;
    private static int IN(JsonElement r, string k, int dflt) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : dflt;
    private static int? INN(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    private static decimal DEC(JsonElement r, string k, decimal dflt) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : dflt;
    private static DateTimeOffset TS(JsonElement r, string k) =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(v.GetString()!) : DateTimeOffset.UtcNow;
    private static DateOnly D(JsonElement r, string k) => DateOnly.Parse(r.GetProperty(k).GetString()!);
    private static DateOnly? DN(JsonElement r, string k) =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? DateOnly.Parse(v.GetString()!) : null;
    private static TimeOnly T(JsonElement r, string k) => TimeOnly.Parse(r.GetProperty(k).GetString()!);
    private static TEnum E<TEnum>(JsonElement r, string k, TEnum dflt) where TEnum : struct =>
        r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && Enum.TryParse<TEnum>(v.GetString(), out var e) ? e : dflt;

    private async Task<int> UpsertMedicines(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "medicine_id");
            var e = await db.Medicines.FindAsync([id], ct) ?? Track(new Medicine { MedicineId = id });
            e.GenericName = S(r, "generic_name");
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertVitalTemplates(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "template_id");
            var e = await db.VitalFieldTemplates.FindAsync([id], ct) ?? Track(new VitalFieldTemplate { TemplateId = id });
            e.Description = S(r, "description");
            e.FormKey = S(r, "form_key");
            e.Unit = S(r, "unit");
            e.Icon = S(r, "icon");
            e.IsDefault = BN(r, "is_default", false);
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertServices(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "service_id");
            var e = await db.Services.FindAsync([id], ct) ?? Track(new Service { ServiceId = id });
            e.Name = S(r, "name");
            e.Category = E(r, "category", ServiceCategory.Consultation);
            e.Description = SN(r, "description");
            e.Price = DEC(r, "price", 0);
            e.IsActive = BN(r, "is_active", true);
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertStaffAccounts(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "staff_id");
            var e = await db.StaffAccounts.FindAsync([id], ct) ?? Track(new StaffAccount { StaffId = id });
            e.UserId = G(r, "user_id");
            e.FullName = S(r, "full_name");
            e.Email = S(r, "email");
            e.ContactNumber = SN(r, "contact_number");
            e.Role = E(r, "role", StaffRole.Staff);
            e.Status = E(r, "status", StaffStatus.Active);
            e.AvatarUrl = SN(r, "avatar_url");
            e.InvitedAt = TS(r, "invited_at");
            e.RevokedAt = r.TryGetProperty("revoked_at", out var rv) && rv.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(rv.GetString()!) : null;
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertPatients(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "patient_id");
            var e = await db.Patients.FindAsync([id], ct) ?? Track(new Patient { PatientId = id });
            e.UserId = GN(r, "user_id");
            e.PatientCode = S(r, "patient_code");
            e.FirstName = S(r, "first_name");
            e.MiddleName = SN(r, "middle_name");
            e.LastName = S(r, "last_name");
            e.DateOfBirth = D(r, "date_of_birth");
            e.Sex = E(r, "sex", SexType.Female);
            e.CivilStatus = SN(r, "civil_status");
            e.Address = SN(r, "address");
            e.City = SN(r, "city");
            e.ZipCode = SN(r, "zip_code");
            e.ContactNumber = SN(r, "contact_number");
            e.Email = S(r, "email");
            e.EmergencyContactName = SN(r, "emergency_contact_name");
            e.EmergencyContactNumber = SN(r, "emergency_contact_number");
            e.EmergencyContactRelationship = SN(r, "emergency_contact_relationship");
            e.BloodType = SN(r, "blood_type");
            e.PhilhealthNumber = SN(r, "philhealth_number");
            e.HmoProvider = SN(r, "hmo_provider");
            e.HmoCardNumber = SN(r, "hmo_card_number");
            e.IsGuest = BN(r, "is_guest", false);
            e.IsEmailVerified = BN(r, "is_email_verified", false);
            e.ConsentedAt = r.TryGetProperty("consented_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(ca.GetString()!) : null;
            e.ConsentVersion = IN(r, "consent_version", 0);
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertDoctors(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "doctor_id");
            var e = await db.Doctors.FindAsync([id], ct) ?? Track(new Doctor { DoctorId = id });
            e.Specialization = S(r, "specialization");
            e.ConsultationFee = DEC(r, "consultation_fee", 0);
            e.Bio = SN(r, "bio");
            e.LicenseNumber = SN(r, "license_number");
            e.PtrNumber = SN(r, "ptr_number");
            e.S2Number = SN(r, "s2_number");
            e.SlotDurationMinutes = IN(r, "slot_duration_minutes", 30);
            e.SlotCapacity = IN(r, "slot_capacity", 1);
            e.DailyPatientLimit = INN(r, "daily_patient_limit");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertDoctorServices(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var d = G(r, "doctor_id");
            var s = G(r, "service_id");
            var e = await db.DoctorServices.FindAsync([d, s], ct) ?? Track(new DoctorService { DoctorId = d, ServiceId = s });
            e.DurationMinutes = IN(r, "duration_minutes", 30);
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertDoctorSchedules(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.DoctorSchedules.FindAsync([id], ct) ?? Track(new DoctorSchedule { Id = id });
            e.DoctorId = G(r, "doctor_id");
            e.DayOfWeek = (short)IN(r, "day_of_week", 0);
            e.IsActive = BN(r, "is_active", true);
            e.StartTime = T(r, "start_time");
            e.EndTime = T(r, "end_time");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertBlockedDates(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.DoctorBlockedDates.FindAsync([id], ct) ?? Track(new DoctorBlockedDate { Id = id });
            e.DoctorId = G(r, "doctor_id");
            e.BlockedDate = D(r, "blocked_date");
            e.Reason = SN(r, "reason");
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertBookings(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "booking_id");
            var e = await db.Bookings.FindAsync([id], ct) ?? Track(new Booking { BookingId = id });
            e.PatientId = G(r, "patient_id");
            e.DoctorId = G(r, "doctor_id");
            e.AppointmentDate = D(r, "appointment_date");
            e.SlotStartTime = T(r, "slot_start_time");
            e.SlotEndTime = T(r, "slot_end_time");
            e.Status = E(r, "status", BookingStatus.Pending);
            e.PaymentMode = E(r, "payment_mode", PaymentMode.PayAtClinic);
            e.QueueNumber = SN(r, "queue_number");
            e.ConsultationFeeSnapshot = DEC(r, "consultation_fee_snapshot", 0);
            e.TotalFee = DEC(r, "total_fee", 0);
            e.AmountDue = DEC(r, "amount_due", 0);
            e.IsWalkIn = BN(r, "is_walk_in", false);
            e.ProofType = r.TryGetProperty("proof_type", out var pt) && pt.ValueKind == JsonValueKind.String && Enum.TryParse<ProofType>(pt.GetString(), out var ptv) ? ptv : null;
            e.ProofValue = SN(r, "proof_value");
            e.ProofSubmittedAt = r.TryGetProperty("proof_submitted_at", out var ps) && ps.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(ps.GetString()!) : null;
            e.CancelledByUserId = GN(r, "cancelled_by_user_id");
            e.CancellationReason = SN(r, "cancellation_reason");
            e.Notes = SN(r, "notes");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertBookingServices(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var b = G(r, "booking_id");
            var s = G(r, "service_id");
            var e = await db.BookingServices.FindAsync([b, s], ct) ?? Track(new BookingService { BookingId = b, ServiceId = s });
            e.PriceAtBooking = DEC(r, "price_at_booking", 0);
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertPayments(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "payment_id");
            var e = await db.Payments.FindAsync([id], ct) ?? Track(new Payment { PaymentId = id });
            e.BookingId = G(r, "booking_id");
            e.Amount = DEC(r, "amount", 0);
            e.Status = E(r, "status", PaymentStatus.Unpaid);
            e.PaymentMethod = r.TryGetProperty("payment_method", out var pm) && pm.ValueKind == JsonValueKind.String && Enum.TryParse<PaymentMethod>(pm.GetString(), out var pmv) ? pmv : null;
            e.ReferenceNumber = SN(r, "reference_number");
            e.OrNumber = SN(r, "or_number");
            e.AmountReceived = r.TryGetProperty("amount_received", out var ar) && ar.ValueKind == JsonValueKind.Number ? ar.GetDecimal() : null;
            e.ConfirmNotes = SN(r, "confirm_notes");
            e.ConfirmedByUserId = GN(r, "confirmed_by_user_id");
            e.ConfirmedAt = r.TryGetProperty("confirmed_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(ca.GetString()!) : null;
            e.WaivedByUserId = GN(r, "waived_by_user_id");
            e.WaivedReason = SN(r, "waived_reason");
            e.WaivedAt = r.TryGetProperty("waived_at", out var wa) && wa.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(wa.GetString()!) : null;
            e.RefundedByUserId = GN(r, "refunded_by_user_id");
            e.RefundAmount = r.TryGetProperty("refund_amount", out var ra) && ra.ValueKind == JsonValueKind.Number ? ra.GetDecimal() : null;
            e.RefundReason = SN(r, "refund_reason");
            e.RefundedAt = r.TryGetProperty("refunded_at", out var rfa) && rfa.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(rfa.GetString()!) : null;
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertDayStatuses(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.DoctorDayStatuses.FindAsync([id], ct) ?? Track(new DoctorDayStatus { Id = id });
            e.DoctorId = G(r, "doctor_id");
            e.StatusDate = D(r, "status_date");
            e.Status = E(r, "status", DoctorDayStatusEnum.Available);
            e.RunningLateMinutes = INN(r, "running_late_minutes");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertConsultations(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "consultation_id");
            var e = await db.Consultations.FindAsync([id], ct) ?? Track(new Consultation { ConsultationId = id });
            e.BookingId = G(r, "booking_id");
            e.PatientId = G(r, "patient_id");
            e.DoctorId = G(r, "doctor_id");
            e.Status = E(r, "status", ConsultationStatus.Draft);
            e.ChiefComplaint = SN(r, "chief_complaint");
            e.Subjective = SN(r, "subjective");
            e.Objective = SN(r, "objective");
            e.Assessment = SN(r, "assessment");
            e.Plan = SN(r, "plan");
            e.DoctorNotes = SN(r, "doctor_notes");
            e.CompletedByUserId = GN(r, "completed_by_user_id");
            e.CompletedAt = r.TryGetProperty("completed_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(ca.GetString()!) : null;
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertConsultationDiagnoses(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.ConsultationDiagnoses.FindAsync([id], ct) ?? Track(new ConsultationDiagnosis { Id = id });
            e.ConsultationId = G(r, "consultation_id");
            e.Icd10Code = SN(r, "icd10_code");
            e.CustomDescription = SN(r, "custom_description");
            e.Type = E(r, "type", DiagnosisType.Primary);
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertVitalReadings(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.PatientVitalReadings.FindAsync([id], ct) ?? Track(new PatientVitalReading { Id = id });
            e.BookingId = G(r, "booking_id");
            e.PatientId = G(r, "patient_id");
            e.TemplateId = G(r, "template_id");
            e.Value = S(r, "value");
            e.RecordedAt = D(r, "recorded_at");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertFollowUps(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.FollowUps.FindAsync([id], ct) ?? Track(new FollowUp { Id = id });
            e.ConsultationId = G(r, "consultation_id");
            e.PatientId = G(r, "patient_id");
            e.DoctorId = G(r, "doctor_id");
            e.FollowUpDate = D(r, "follow_up_date");
            e.Reason = SN(r, "reason");
            e.Instructions = SN(r, "instructions");
            e.ReminderEnabled = BN(r, "reminder_enabled", true);
            e.Status = E(r, "status", FollowUpStatus.Pending);
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertRxGroups(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "group_id");
            var e = await db.PrescriptionGroups.FindAsync([id], ct) ?? Track(new PrescriptionGroup { GroupId = id });
            e.PatientId = G(r, "patient_id");
            e.DoctorId = G(r, "doctor_id");
            e.BookingId = G(r, "booking_id");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertRxLineItems(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.PrescriptionLineItems.FindAsync([id], ct) ?? Track(new PrescriptionLineItem { Id = id });
            e.GroupId = G(r, "group_id");
            e.MedicineId = G(r, "medicine_id");
            e.GenericName = S(r, "generic_name");
            e.Dosage = S(r, "dosage");
            e.Quantity = S(r, "quantity");
            e.Instruction = SN(r, "instruction");
            e.IsControlledSubstance = BN(r, "is_controlled_substance", false);
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertRxTemplates(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "template_id");
            var e = await db.PrescriptionTemplates.FindAsync([id], ct) ?? Track(new PrescriptionTemplate { TemplateId = id });
            e.DoctorId = G(r, "doctor_id");
            e.Title = S(r, "title");
            e.IsSystemTemplate = BN(r, "is_system_template", false);
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertRxTemplateItems(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.PrescriptionTemplateItems.FindAsync([id], ct) ?? Track(new PrescriptionTemplateItem { Id = id });
            e.TemplateId = G(r, "template_id");
            e.MedicineId = G(r, "medicine_id");
            e.GenericName = S(r, "generic_name");
            e.Dosage = S(r, "dosage");
            e.Quantity = S(r, "quantity");
            e.Instruction = SN(r, "instruction");
            e.IsControlledSubstance = BN(r, "is_controlled_substance", false);
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertFavMedicines(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.DoctorFavoriteMedicines.FindAsync([id], ct) ?? Track(new DoctorFavoriteMedicine { Id = id });
            e.DoctorId = G(r, "doctor_id");
            e.MedicineId = G(r, "medicine_id");
            e.GenericName = S(r, "generic_name");
            e.Dosage = S(r, "dosage");
            e.Quantity = S(r, "quantity");
            e.Instruction = SN(r, "instruction");
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertSoapTemplates(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.SoapTemplates.FindAsync([id], ct) ?? Track(new SoapTemplate { Id = id });
            e.DoctorId = G(r, "doctor_id");
            e.Title = S(r, "title");
            e.IsSystemTemplate = BN(r, "is_system_template", false);
            e.ChiefComplaint = SN(r, "chief_complaint");
            e.Subjective = SN(r, "subjective");
            e.Objective = SN(r, "objective");
            e.Assessment = SN(r, "assessment");
            e.Plan = SN(r, "plan");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertSoapPhrases(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.SoapPhrases.FindAsync([id], ct) ?? Track(new SoapPhrase { Id = id });
            e.DoctorId = G(r, "doctor_id");
            e.Field = E(r, "field", SoapField.Subjective);
            e.Label = S(r, "label");
            e.Body = S(r, "body");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertReviews(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "review_id");
            var e = await db.Reviews.FindAsync([id], ct) ?? Track(new Review { ReviewId = id });
            e.BookingId = G(r, "booking_id");
            e.DoctorId = G(r, "doctor_id");
            e.PatientId = G(r, "patient_id");
            e.Rating = (short)IN(r, "rating", 5);
            e.Comment = SN(r, "comment");
            e.CreatedAt = TS(r, "created_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertVaccinations(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.PatientVaccinations.FindAsync([id], ct) ?? Track(new PatientVaccination { Id = id });
            e.PatientId = G(r, "patient_id");
            e.ConsultationId = GN(r, "consultation_id");
            e.VaccineName = S(r, "vaccine_name");
            e.Manufacturer = SN(r, "manufacturer");
            e.DoseNumber = r.TryGetProperty("dose_number", out var dn) && dn.ValueKind == JsonValueKind.Number ? (short)dn.GetInt32() : null;
            e.Route = SN(r, "route");
            e.Site = SN(r, "site");
            e.LotNumber = SN(r, "lot_number");
            e.ExpiryDate = DN(r, "expiry_date");
            e.AdministeredDate = DN(r, "administered_date");
            e.AdministeredBy = GN(r, "administered_by");
            e.NextDoseDate = DN(r, "next_dose_date");
            e.Status = E(r, "status", VaccinationStatus.Scheduled);
            e.Source = E(r, "source", VaccinationSource.AdministeredInClinic);
            e.Notes = SN(r, "notes");
            e.ReactionNotes = SN(r, "reaction_notes");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertDocuments(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.PatientDocuments.FindAsync([id], ct) ?? Track(new PatientDocument { Id = id });
            e.PatientId = G(r, "patient_id");
            e.BookingId = G(r, "booking_id");
            e.ConsultationId = GN(r, "consultation_id");
            e.FileName = S(r, "file_name");
            e.FileSize = r.TryGetProperty("file_size", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : null;
            e.FileContentType = SN(r, "file_content_type");
            e.Title = SN(r, "title");
            e.Description = SN(r, "description");
            e.FileUrl = S(r, "file_url");
            e.UploadedByUserId = GN(r, "uploaded_by_user_id");
            e.UploadedAt = TS(r, "uploaded_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertLabResults(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.PatientLabResults.FindAsync([id], ct) ?? Track(new PatientLabResult { Id = id });
            e.PatientId = G(r, "patient_id");
            e.BookingId = G(r, "booking_id");
            e.ConsultationId = GN(r, "consultation_id");
            e.LabOrderId = GN(r, "lab_order_id");
            e.FileName = S(r, "file_name");
            e.FileContentType = SN(r, "file_content_type");
            e.ResultTitle = SN(r, "result_title");
            e.ResultText = SN(r, "result_text");
            e.Status = S(r, "status") is { Length: > 0 } st ? st : "Completed";
            e.FileUrl = S(r, "file_url");
            e.UploadedAt = TS(r, "uploaded_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertAnnouncements(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.Announcements.FindAsync([id], ct) ?? Track(new Announcement { Id = id });
            e.Title = S(r, "title");
            e.Body = S(r, "body");
            e.IsActive = BN(r, "is_active", true);
            e.PostedByUserId = GN(r, "posted_by_user_id");
            e.CreatedAt = TS(r, "created_at");
            e.UpdatedAt = TS(r, "updated_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private async Task<int> UpsertAuditLogs(List<JsonElement> rows, CancellationToken ct)
    {
        foreach (var r in rows)
        {
            var id = G(r, "id");
            var e = await db.AuditLogs.FindAsync([id], ct) ?? Track(new AuditLog { Id = id });
            e.EntityType = E(r, "entity_type", AuditEntityType.Consultation);
            e.EntityId = G(r, "entity_id");
            e.Action = S(r, "action");
            e.PerformedByUserId = GN(r, "performed_by_user_id");
            e.Details = SN(r, "details");
            e.PerformedAt = TS(r, "performed_at");
        }
        return await db.SaveChangesAsync(ct);
    }

    private T Track<T>(T entity) where T : class
    {
        db.Add(entity);
        return entity;
    }
}
