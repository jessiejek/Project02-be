using ClinicApp.Api.Auditing;
using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record UpsertConsultationRequest(
    Guid PatientId,
    Guid DoctorId,
    ConsultationStatus Status,
    string? ChiefComplaint,
    string? Subjective,
    string? Objective,
    string? Assessment,
    string? Plan,
    string? DoctorNotes,
    // §16.6 — doctor picks the fee line at consultation. Null = leave the
    // booking's current value untouched.
    VisitType? VisitType = null,
    bool? MedCertRequested = null,
    /// <summary>'Senior' | 'PWD' | null.</summary>
    string? DiscountCategory = null,
    // §16.6 Professional Fee decision. 'Charge' | 'Waive' | null (not yet decided).
    string? PfDecision = null,
    decimal? PfAmount = null,
    string? PfWaiveReason = null);

public record DiagnosisInput(string? Icd10Code, string? CustomDescription, DiagnosisType Type);

/// <summary>snake_case wire (contract §4). Consultation is 1:1 with a booking —
/// upsert conflict key is booking_id. Diagnoses are replace-all per contract §10.</summary>
[ApiController]
[Authorize]
[Route("api/consultations")]
public class ConsultationsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    // §6 embeds: bookings(appointment_date, doctor_id), doctors(staff_accounts(full_name)),
    // consultation_diagnoses(custom_description, type), follow_ups(follow_up_date, instructions).
    private IQueryable<Consultation> WithEmbeds() =>
        db.Consultations.AsNoTracking()
            .Include(c => c.Booking)
            .Include(c => c.Doctor).ThenInclude(d => d!.StaffAccount)
            .Include(c => c.ConsultationDiagnoses)
            .Include(c => c.FollowUp);

    [HttpGet]
    public async Task<ActionResult<List<Consultation>>> GetAll(
        [FromQuery] Guid? patientId, [FromQuery] Guid? doctorId, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();

        var q = WithEmbeds();
        if (patientId is not null) q = q.Where(c => c.PatientId == patientId);
        if (doctorId is not null) q = q.Where(c => c.DoctorId == doctorId);
        if (bookingId is not null) q = q.Where(c => c.BookingId == bookingId);
        return Ok(await q.OrderByDescending(c => c.CreatedAt).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Consultation>> GetById(Guid id, CancellationToken ct)
    {
        var c = await WithEmbeds().SingleOrDefaultAsync(x => x.ConsultationId == id, ct);
        if (c is null) return NotFound();
        return (await actors.ResolveAsync(User, ct)).CanAccessPatient(c.PatientId) ? Ok(c) : NotFound();
    }

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<Consultation>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var c = await WithEmbeds().SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (c is null) return NotFound();
        return (await actors.ResolveAsync(User, ct)).CanAccessPatient(c.PatientId) ? Ok(c) : NotFound();
    }

    /// <summary>Upsert on booking_id (contract §10 — the consultation page saves this way).</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<Consultation>> UpsertByBooking(Guid bookingId, UpsertConsultationRequest req, CancellationToken ct)
    {
        // The booking is the source of truth for who the consultation is for and with. Never
        // trust patient_id / doctor_id from the body, and a doctor only writes their own visits.
        var ownerBooking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (ownerBooking is null) return NotFound(new { message = "Booking not found." });
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(ownerBooking.DoctorId)) return Forbid();
        if (req.PatientId != ownerBooking.PatientId || req.DoctorId != ownerBooking.DoctorId)
            return BadRequest(new { message = "Patient / doctor do not match the booking." });

        var now = DateTimeOffset.UtcNow;
        var c = await db.Consultations.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        var isNew = c is null;
        var before = isNew ? null : new
        {
            c!.ChiefComplaint, c.Subjective, c.Objective, c.Assessment, c.Plan, c.DoctorNotes,
            c.PfDecision, c.PfAmount, c.PfWaiveReason
        };
        if (c is null)
        {
            c = new Consultation { ConsultationId = Guid.NewGuid(), BookingId = bookingId, CreatedAt = now };
            db.Consultations.Add(c);
        }
        else if (c.Status is ConsultationStatus.Completed or ConsultationStatus.Amended)
        {
            // §17.3 #15 — a finalized record is append-only via Amended: it can never go back to
            // Draft, and it can't be edited while still claiming to be "Completed".
            if (req.Status == ConsultationStatus.Draft)
                return Conflict(new { message = "A completed consultation can only be amended." });
            if (req.Status == ConsultationStatus.Completed)
            {
                // Re-sending the same "Completed" save is a harmless retry / double-click (the doctor
                // page's save is several calls, so a retry after a partial failure is normal): a no-op.
                // Only *different* content has to go through Amend.
                if (SameContent(c, req, ownerBooking)) return Ok(c);
                return Conflict(new { message = "A completed consultation must be amended to change clinical content." });
            }
            // req.Status == Amended — allowed
        }
        c.PatientId = req.PatientId;
        c.DoctorId = req.DoctorId;
        c.Status = req.Status;
        c.ChiefComplaint = req.ChiefComplaint;
        c.Subjective = req.Subjective;
        c.Objective = req.Objective;
        c.Assessment = req.Assessment;
        c.Plan = req.Plan;
        c.DoctorNotes = req.DoctorNotes;
        // §16.6 — the PF decision travels with the consultation row.
        c.PfDecision = string.IsNullOrWhiteSpace(req.PfDecision) ? null : req.PfDecision.Trim();
        c.PfAmount = c.PfDecision == "Charge" ? req.PfAmount : null;
        c.PfWaiveReason = c.PfDecision == "Waive"
            ? (string.IsNullOrWhiteSpace(req.PfWaiveReason) ? null : req.PfWaiveReason.Trim())
            : null;
        if (req.Status == ConsultationStatus.Completed && c.CompletedAt is null)
        {
            c.CompletedAt = now;
            c.CompletedByUserId = CurrentUserId();
        }
        c.UpdatedAt = now;

        // §16.6 — apply the doctor's fee-line choices to the booking and
        // recompute the flat clinic fee server-side.
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is not null)
        {
            if (req.VisitType is { } vt) booking.VisitType = vt;
            if (req.MedCertRequested is { } mc) booking.MedCertRequested = mc;
            if (req.DiscountCategory is not null)
                booking.DiscountCategory = string.IsNullOrWhiteSpace(req.DiscountCategory) ? null : req.DiscountCategory.Trim();

            var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
            if (settings is not null)
            {
                var fee = ClinicApp.Domain.ClinicFees.Compute(
                    settings, booking.VisitType, booking.MedCertRequested, booking.DiscountCategory);
                booking.ConsultationFeeSnapshot = fee.Subtotal;
                booking.DiscountAmount = fee.DiscountAmount;
                booking.TotalFee = fee.Total;
                booking.AmountDue = fee.Total;
                booking.UpdatedAt = now;
            }
        }

        var details = isNew
            ? AuditLogWriter.DiffDetails(
                ("Chief complaint", null, c.ChiefComplaint), ("Subjective", null, c.Subjective),
                ("Objective", null, c.Objective), ("Assessment", null, c.Assessment), ("Plan", null, c.Plan),
                ("Doctor notes", null, c.DoctorNotes), ("PF decision", null, c.PfDecision))
            : AuditLogWriter.DiffDetails(
                ("Chief complaint", before!.ChiefComplaint, c.ChiefComplaint), ("Subjective", before.Subjective, c.Subjective),
                ("Objective", before.Objective, c.Objective), ("Assessment", before.Assessment, c.Assessment),
                ("Plan", before.Plan, c.Plan), ("Doctor notes", before.DoctorNotes, c.DoctorNotes),
                ("PF decision", before.PfDecision, c.PfDecision), ("PF amount", before.PfAmount, c.PfAmount),
                ("PF waive reason", before.PfWaiveReason, c.PfWaiveReason));
        AuditLogWriter.Add(db, AuditEntityType.Consultation, c.ConsultationId, req.Status.ToString(), CurrentUserId(), details);

        await db.SaveChangesAsync(ct);
        return Ok(c);
    }

    [HttpGet("{id:guid}/diagnoses")]
    public async Task<ActionResult<List<ConsultationDiagnosis>>> GetDiagnoses(Guid id, CancellationToken ct)
    {
        var owner = await db.Consultations.AsNoTracking().Where(c => c.ConsultationId == id)
            .Select(c => (Guid?)c.PatientId).SingleOrDefaultAsync(ct);
        if (owner is null || !(await actors.ResolveAsync(User, ct)).CanAccessPatient(owner.Value)) return NotFound();
        return Ok(await db.ConsultationDiagnoses.AsNoTracking().Where(d => d.ConsultationId == id).ToListAsync(ct));
    }

    /// <summary>Replace-all (contract §10).</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("{id:guid}/diagnoses")]
    public async Task<ActionResult<List<ConsultationDiagnosis>>> ReplaceDiagnoses(Guid id, List<DiagnosisInput> diagnoses, CancellationToken ct)
    {
        var consult = await db.Consultations.AsNoTracking().SingleOrDefaultAsync(c => c.ConsultationId == id, ct);
        if (consult is null) return NotFound(new { message = "Consultation not found." });
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(consult.DoctorId)) return Forbid();

        // §17.3 #15 — the doctor page completes a visit as "save consultation as Completed", then
        // "save diagnoses" moments later. So a freshly Completed consultation must still accept its
        // diagnoses; once that completion window has passed the record is sealed and a change has to
        // go through Amend (status Amended first). This never changes the consultation's status
        // itself — completing stays Completed, amending stays Amended.
        if (consult.Status == ConsultationStatus.Completed &&
            (consult.CompletedAt is null || DateTimeOffset.UtcNow - consult.CompletedAt.Value > CompletionWindow))
        {
            return Conflict(new { message = "A completed consultation must be amended to change its diagnoses." });
        }

        var existingRows = await db.ConsultationDiagnoses.Where(d => d.ConsultationId == id).ToListAsync(ct);
        if (consult.Status == ConsultationStatus.Amended)
        {
            var beforeText = DiagnosisText(existingRows.Select(d => (d.Icd10Code, d.CustomDescription, d.Type)));
            var afterText = DiagnosisText(diagnoses.Select(d => (d.Icd10Code, d.CustomDescription, d.Type)));
            var change = AuditLogWriter.DiffDetails(("Diagnoses", beforeText, afterText));
            if (change is not null)
                AuditLogWriter.Add(db, AuditEntityType.Consultation, id, "Diagnoses amended", CurrentUserId(), change);
        }

        db.ConsultationDiagnoses.RemoveRange(existingRows);

        var now = DateTimeOffset.UtcNow;
        foreach (var d in diagnoses)
        {
            db.ConsultationDiagnoses.Add(new ConsultationDiagnosis
            {
                Id = Guid.NewGuid(),
                ConsultationId = id,
                Icd10Code = string.IsNullOrWhiteSpace(d.Icd10Code) ? null : d.Icd10Code,
                CustomDescription = string.IsNullOrWhiteSpace(d.CustomDescription) ? null : d.CustomDescription,
                Type = d.Type,
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(await db.ConsultationDiagnoses.AsNoTracking().Where(x => x.ConsultationId == id).ToListAsync(ct));
    }

    /// <summary>How long after completion a consultation still accepts its child rows (diagnoses)
    /// without being amended — long enough for the doctor page's multi-call save and a retry,
    /// short enough that a finished record is effectively sealed.</summary>
    internal static readonly TimeSpan CompletionWindow = TimeSpan.FromMinutes(2);

    private static string DiagnosisText(IEnumerable<(string? Code, string? Description, DiagnosisType Type)> rows) =>
        string.Join("; ", rows
            .Select(r => $"{r.Type}: {(string.IsNullOrWhiteSpace(r.Description) ? r.Code : r.Description)}")
            .OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>True when re-saving <paramref name="req"/> would change nothing on the stored
    /// consultation or on the fee-driving booking fields (same normalization as the save itself).</summary>
    private static bool SameContent(Consultation c, UpsertConsultationRequest req, Booking booking)
    {
        static bool Eq(string? a, string? b) => (a ?? "") == (b ?? "");

        var pfDecision = string.IsNullOrWhiteSpace(req.PfDecision) ? null : req.PfDecision.Trim();
        var pfAmount = pfDecision == "Charge" ? req.PfAmount : null;
        var pfReason = pfDecision == "Waive" ? (string.IsNullOrWhiteSpace(req.PfWaiveReason) ? null : req.PfWaiveReason.Trim()) : null;

        var discount = req.DiscountCategory is null ? booking.DiscountCategory
            : string.IsNullOrWhiteSpace(req.DiscountCategory) ? null : req.DiscountCategory.Trim();

        return Eq(c.ChiefComplaint, req.ChiefComplaint) && Eq(c.Subjective, req.Subjective)
            && Eq(c.Objective, req.Objective) && Eq(c.Assessment, req.Assessment)
            && Eq(c.Plan, req.Plan) && Eq(c.DoctorNotes, req.DoctorNotes)
            && Eq(c.PfDecision, pfDecision) && c.PfAmount == pfAmount && Eq(c.PfWaiveReason, pfReason)
            && (req.VisitType is null || req.VisitType == booking.VisitType)
            && (req.MedCertRequested is null || req.MedCertRequested == booking.MedCertRequested)
            && Eq(booking.DiscountCategory, discount);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var gid) ? gid : null;
    }
}
