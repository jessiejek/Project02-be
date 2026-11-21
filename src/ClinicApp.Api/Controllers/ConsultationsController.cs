//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Hubs; //JJQR - AI (Nov 21, 2026)
using ClinicApp.Api.Security;
using ClinicApp.Domain; //JJQR - AI (Nov 21, 2026) - PaymentStatusMachine
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR; //JJQR - AI (Nov 21, 2026)
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
    string? PfWaiveReason = null,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? ConsultationId = null
    //JJQR - END
    );

public record DiagnosisInput(string? Icd10Code, string? CustomDescription, DiagnosisType Type,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? Id = null
    //JJQR - END
    );

/// <summary>snake_case wire (contract §4). Consultation is 1:1 with a booking —
/// upsert conflict key is booking_id. Diagnoses are replace-all per contract §10.</summary>
[ApiController]
[Authorize]
[Route("api/consultations")]
public class ConsultationsController(ClinicAppDbContext db, ActorResolver actors, IHubContext<ClinicHub> hub) : ControllerBase
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
        //JJQR - AI (Nov 19, 2026) - a Doctor only ever lists their OWN rows: their id replaces whatever ?doctorId= says (Staff / Admin unchanged)
        if (actor.IsDoctor)
        {
            if (actor.StaffId is not { } ownDoctor) return Forbid();
            doctorId = ownDoctor;
        }
        //JJQR - END

        var q = WithEmbeds();
        if (patientId is not null) q = q.Where(c => c.PatientId == patientId);
        if (doctorId is not null) q = q.Where(c => c.DoctorId == doctorId);
        if (bookingId is not null) q = q.Where(c => c.BookingId == bookingId);
        var list = await q.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        foreach (var pid in list.Select(c => c.PatientId).Distinct().Take(20)) await AccessLog.RecordAsync(db, actor, pid, "Consultations", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Consultation>> GetById(Guid id, CancellationToken ct)
    {
        var c = await WithEmbeds().SingleOrDefaultAsync(x => x.ConsultationId == id, ct);
        if (c is null) return NotFound();
        var actorById = await actors.ResolveAsync(User, ct);
        if (!actorById.CanAccessPatient(c.PatientId)) return NotFound();
        await AccessLog.RecordAsync(db, actorById, c.PatientId, "Consultations", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(c);
    }

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<Consultation>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var c = await WithEmbeds().SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (c is null) return NotFound();
        var actorByBooking = await actors.ResolveAsync(User, ct);
        if (!actorByBooking.CanAccessPatient(c.PatientId)) return NotFound();
        await AccessLog.RecordAsync(db, actorByBooking, c.PatientId, "Consultations", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(c);
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

        if (!ClinicApp.Domain.QueueLane.IsValidDiscount(req.DiscountCategory)) //JJQR - AI (Nov 21, 2026) - only Senior / PWD
            return BadRequest(new { message = "Discount must be Senior or PWD." });
        //JJQR - AI (Nov 21, 2026) - the doctor's Charge amount becomes what the front desk collects, so it must be a sane number (waiving is done with Waive, not a zero charge)
        if (string.Equals(req.PfDecision?.Trim(), "Charge", StringComparison.Ordinal) && req.PfAmount is { } chargeAmount && (chargeAmount <= 0 || chargeAmount > 100_000m))
            return BadRequest(new { message = "The professional fee to charge must be more than ₱0 and at most ₱100,000. To waive the fee, choose Waive." });
        //JJQR - END

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
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            if (await ClientIds.TakenAsync<Consultation>(db, req.ConsultationId, ct)) return ClientIds.Conflict();
            c = new Consultation { ConsultationId = ClientIds.Use(req.ConsultationId), BookingId = bookingId, CreatedAt = now };
            //JJQR - END
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

        //JJQR - AI (Nov 21, 2026) - once the payment is settled, an amend must not silently contradict it: a fee that was waived cannot become a Charge, a paid fee cannot be waived, and a paid amount cannot be
        // re-stated. Only a decision that was really REVERSED is refused (a visit with no stored decision, or the same decision, amends as before).
        if (c is not null && c.PfDecision is not null && (req.Status == ConsultationStatus.Completed || req.Status == ConsultationStatus.Amended))
        {
            var settled = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
            var newDecision = string.IsNullOrWhiteSpace(req.PfDecision) ? null : req.PfDecision.Trim();
            if (settled is not null && settled.Status != PaymentStatus.Unpaid && !string.Equals(c.PfDecision, newDecision, StringComparison.Ordinal))
            {
                string? conflict = settled.Status switch
                {
                    PaymentStatus.Waived when newDecision == "Charge" =>
                        "This visit's professional fee was already waived, so it can't be changed to Charge. Ask an admin if the patient should be billed.",
                    PaymentStatus.Paid or PaymentStatus.Refunded when newDecision == "Waive" =>
                        "This visit's professional fee was already paid, so it can't be waived. Refund the payment first.",
                    _ => null
                };
                if (conflict is not null) return Conflict(new { message = conflict });
            }
            if (settled is not null && settled.Status == PaymentStatus.Paid && newDecision == "Charge"
                && req.PfAmount is { } restated && restated != settled.Amount)
                return Conflict(new { message = $"This visit's professional fee was already paid (₱{settled.Amount:0.##}), so the amount can't be changed to ₱{restated:0.##}." });
        }
        //JJQR - END

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
        Payment? pay = null; //JJQR - AI (Nov 21, 2026) - loaded once: used for the amount to collect and for the waive below
        if (booking is not null)
        {
            pay = await db.Payments.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
            if (req.VisitType is { } vt) booking.VisitType = vt;
            if (req.MedCertRequested is { } mc) booking.MedCertRequested = mc;
            if (req.DiscountCategory is not null)
                booking.DiscountCategory = ClinicApp.Domain.QueueLane.NormalizeDiscount(req.DiscountCategory);

            var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
            if (settings is not null)
            {
                var fee = ClinicApp.Domain.ClinicFees.Compute(
                    settings, booking.VisitType, booking.MedCertRequested, booking.DiscountCategory);
                booking.ConsultationFeeSnapshot = fee.Subtotal;
                booking.DiscountAmount = fee.DiscountAmount;
                //JJQR - AI (Nov 21, 2026) - what the front desk collects = the doctor's Charge amount when one was set, otherwise the clinic fee. It used to be ALWAYS the clinic fee, so a
                // doctor charging ₱300 still had ₱450 collected; and the payment's own amount (checked when staff record the payment) was never updated after check-in.
                // Only while nothing has been settled: money already paid / waived / refunded is never rewritten.
                var amountToCollect = c.PfDecision == "Charge" && c.PfAmount is { } doctorAmount ? doctorAmount : fee.Total;
                if (pay is null || pay.Status == PaymentStatus.Unpaid)
                {
                    booking.TotalFee = amountToCollect;
                    booking.AmountDue = amountToCollect;
                    if (pay is not null) pay.Amount = amountToCollect;
                }
                //JJQR - END
                booking.UpdatedAt = now;
            }
        }

        //JJQR - AI (Nov 21, 2026) - the doctor's Waive decision used to be only a note on the consultation: the payment stayed "Unpaid", so the front desk was asked to collect the fee anyway.
        // When the visit is FINALISED (Completed / Amended — never on a draft, because Waived cannot be undone) with a Waive decision and a reason, an unpaid payment is waived here.
        Payment? waivedPayment = null;
        if (booking is not null && c.PfDecision == "Waive" && !string.IsNullOrWhiteSpace(c.PfWaiveReason)
            && (req.Status == ConsultationStatus.Completed || req.Status == ConsultationStatus.Amended))
        {
            if (pay is not null && pay.Status == PaymentStatus.Unpaid && PaymentStatusMachine.CanTransition(pay.Status, PaymentStatus.Waived))
            {
                pay.Status = PaymentStatus.Waived;
                pay.WaivedReason = c.PfWaiveReason;
                pay.WaivedByUserId = CurrentUserId();
                pay.WaivedAt = now;
                booking.AmountDue = 0;
                booking.UpdatedAt = now;
                AuditLogWriter.Add(db, AuditEntityType.Payment, pay.PaymentId, "Waived (doctor decision)", CurrentUserId(),
                    "Professional fee waived on the doctor's decision when the visit was completed.");
                waivedPayment = pay;
            }
        }
        //JJQR - END

        var details = isNew
            ? AuditLogWriter.ChangedFields( // JJQR - AI (Nov 20, 2026) - PV-9: which fields, not their clinical text // JJQR - END
                ("Chief complaint", null, c.ChiefComplaint), ("Subjective", null, c.Subjective),
                ("Objective", null, c.Objective), ("Assessment", null, c.Assessment), ("Plan", null, c.Plan),
                ("Doctor notes", null, c.DoctorNotes), ("PF decision", null, c.PfDecision))
            : AuditLogWriter.ChangedFields(
                ("Chief complaint", before!.ChiefComplaint, c.ChiefComplaint), ("Subjective", before.Subjective, c.Subjective),
                ("Objective", before.Objective, c.Objective), ("Assessment", before.Assessment, c.Assessment),
                ("Plan", before.Plan, c.Plan), ("Doctor notes", before.DoctorNotes, c.DoctorNotes),
                ("PF decision", before.PfDecision, c.PfDecision), ("PF amount", before.PfAmount, c.PfAmount),
                ("PF waive reason", before.PfWaiveReason, c.PfWaiveReason));
        AuditLogWriter.Add(db, AuditEntityType.Consultation, c.ConsultationId, req.Status.ToString(), CurrentUserId(), details);

        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026) - tell the front desk (and the doctor's other screens) straight away that this visit no longer needs collecting
        if (waivedPayment is not null)
        {
            var payload = new { booking_id = bookingId, payment_id = waivedPayment.PaymentId, status = waivedPayment.Status.ToString() };
            await Task.WhenAll(
                hub.Clients.Group("staff").SendAsync("PaymentUpdated", payload, ct),
                hub.Clients.Group($"doctor:{c.DoctorId}").SendAsync("PaymentUpdated", payload, ct));
        }
        //JJQR - END
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
                //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
                Id = await ClientIds.FreshAsync<ConsultationDiagnosis>(db, d.Id, ct),
                //JJQR - END
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
            : ClinicApp.Domain.QueueLane.NormalizeDiscount(req.DiscountCategory);

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
