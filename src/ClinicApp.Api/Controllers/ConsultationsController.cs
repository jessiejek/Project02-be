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
    string? DiscountCategory = null);

public record DiagnosisInput(string? Icd10Code, string? CustomDescription, DiagnosisType Type);

/// <summary>snake_case wire (contract §4). Consultation is 1:1 with a booking —
/// upsert conflict key is booking_id. Diagnoses are replace-all per contract §10.</summary>
[ApiController]
[Authorize]
[Route("api/consultations")]
public class ConsultationsController(ClinicAppDbContext db) : ControllerBase
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
        return c is null ? NotFound() : Ok(c);
    }

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<Consultation>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var c = await WithEmbeds().SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        return c is null ? NotFound() : Ok(c);
    }

    /// <summary>Upsert on booking_id (contract §10 — the consultation page saves this way).</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<Consultation>> UpsertByBooking(Guid bookingId, UpsertConsultationRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var c = await db.Consultations.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (c is null)
        {
            c = new Consultation { ConsultationId = Guid.NewGuid(), BookingId = bookingId, CreatedAt = now };
            db.Consultations.Add(c);
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

        await db.SaveChangesAsync(ct);
        return Ok(c);
    }

    [HttpGet("{id:guid}/diagnoses")]
    public async Task<ActionResult<List<ConsultationDiagnosis>>> GetDiagnoses(Guid id, CancellationToken ct) =>
        Ok(await db.ConsultationDiagnoses.AsNoTracking().Where(d => d.ConsultationId == id).ToListAsync(ct));

    /// <summary>Replace-all (contract §10).</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("{id:guid}/diagnoses")]
    public async Task<ActionResult<List<ConsultationDiagnosis>>> ReplaceDiagnoses(Guid id, List<DiagnosisInput> diagnoses, CancellationToken ct)
    {
        var existing = db.ConsultationDiagnoses.Where(d => d.ConsultationId == id);
        db.ConsultationDiagnoses.RemoveRange(existing);

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

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var gid) ? gid : null;
    }
}
