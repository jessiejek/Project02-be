using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record UpsertMedicalCertificateRequest(
    Guid PatientId,
    Guid DoctorId,
    DateOnly? IssueDate,
    string? PatientAddressSnapshot,
    string? ExaminedAt,
    DateOnly? ExaminationDateFrom,
    DateOnly? ExaminationDateTo,
    string? DiagnosisText,
    string? Recommendations,
    string? PurposeException,
    DateOnly? ComeBackOn);

/// <summary>medical_certificates (§16.8 Form 2). One per consultation — upsert
/// conflict key is consultation_id. Doctor license / PTR are read live from
/// `doctors` at print time, not stored here.</summary>
[ApiController]
[Authorize]
[Route("api/medical-certificates")]
public class MedicalCertificatesController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MedicalCertificate>>> GetAll(
        [FromQuery] Guid? patientId, [FromQuery] Guid? doctorId, [FromQuery] Guid? consultationId, CancellationToken ct)
    {
        var q = db.MedicalCertificates.AsNoTracking().AsQueryable();
        if (patientId is not null) q = q.Where(m => m.PatientId == patientId);
        if (doctorId is not null) q = q.Where(m => m.DoctorId == doctorId);
        if (consultationId is not null) q = q.Where(m => m.ConsultationId == consultationId);
        return Ok(await q.OrderByDescending(m => m.IssueDate).ToListAsync(ct));
    }

    [HttpGet("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<MedicalCertificate>> GetByConsultation(Guid consultationId, CancellationToken ct)
    {
        var m = await db.MedicalCertificates.AsNoTracking().SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        return m is null ? NotFound() : Ok(m);
    }

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<MedicalCertificate>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var consultationId = await db.Consultations
            .Where(c => c.BookingId == bookingId)
            .Select(c => (Guid?)c.ConsultationId)
            .SingleOrDefaultAsync(ct);
        if (consultationId is null) return NotFound();
        var m = await db.MedicalCertificates.AsNoTracking().SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        return m is null ? NotFound() : Ok(m);
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<MedicalCertificate>> UpsertByConsultation(
        Guid consultationId, UpsertMedicalCertificateRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var m = await db.MedicalCertificates.SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        if (m is null)
        {
            m = new MedicalCertificate { CertificateId = Guid.NewGuid(), ConsultationId = consultationId, CreatedAt = now };
            db.MedicalCertificates.Add(m);
        }
        m.PatientId = req.PatientId;
        m.DoctorId = req.DoctorId;
        m.IssueDate = req.IssueDate ?? ClinicApp.Domain.ClinicClock.Today;
        m.PatientAddressSnapshot = req.PatientAddressSnapshot;
        m.ExaminedAt = req.ExaminedAt;
        m.ExaminationDateFrom = req.ExaminationDateFrom;
        m.ExaminationDateTo = req.ExaminationDateTo;
        m.DiagnosisText = req.DiagnosisText;
        m.Recommendations = req.Recommendations;
        m.PurposeException = req.PurposeException;
        m.ComeBackOn = req.ComeBackOn;
        m.IssuedByUserId = CurrentUserId();
        m.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return Ok(m);
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpDelete("by-consultation/{consultationId:guid}")]
    public async Task<IActionResult> DeleteByConsultation(Guid consultationId, CancellationToken ct)
    {
        var m = await db.MedicalCertificates.SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        if (m is null) return NotFound();
        db.MedicalCertificates.Remove(m);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
