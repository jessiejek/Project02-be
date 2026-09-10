using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>One requested test. `LabTestId` set = came from a catalog checkbox;
/// null = handwritten. `TestName` is always the denormalised label (like the
/// prescription line items keep `generic_name`).</summary>
public record LabOrderInput(
    Guid? LabTestId,
    string TestName,
    string? ClinicalIndication,
    string? SpecimenType,
    string? Notes);

/// <summary>lab_orders (§16.8 Form 3). A consultation's lab request is a
/// replace-all set keyed on consultation_id, same shape as
/// consultation_diagnoses.</summary>
[ApiController]
[Authorize]
[Route("api/lab-orders")]
public class LabOrdersController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LabOrder>>> GetAll(
        [FromQuery] Guid? consultationId, [FromQuery] Guid? patientId, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var q = db.LabOrders.AsNoTracking().AsQueryable();
        if (consultationId is not null) q = q.Where(l => l.ConsultationId == consultationId);
        if (patientId is not null) q = q.Where(l => l.PatientId == patientId);
        if (bookingId is not null)
        {
            var cid = await db.Consultations.Where(c => c.BookingId == bookingId)
                .Select(c => (Guid?)c.ConsultationId).SingleOrDefaultAsync(ct);
            q = q.Where(l => l.ConsultationId == cid);
        }
        return Ok(await q.OrderBy(l => l.CreatedAt).ToListAsync(ct));
    }

    [HttpGet("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<List<LabOrder>>> GetByConsultation(Guid consultationId, CancellationToken ct) =>
        Ok(await db.LabOrders.AsNoTracking()
            .Where(l => l.ConsultationId == consultationId)
            .OrderBy(l => l.CreatedAt).ToListAsync(ct));

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<List<LabOrder>>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var cid = await db.Consultations.Where(c => c.BookingId == bookingId)
            .Select(c => (Guid?)c.ConsultationId).SingleOrDefaultAsync(ct);
        if (cid is null) return Ok(new List<LabOrder>());
        return Ok(await db.LabOrders.AsNoTracking()
            .Where(l => l.ConsultationId == cid).OrderBy(l => l.CreatedAt).ToListAsync(ct));
    }

    /// <summary>Replace-all for one consultation (contract §10). Patient / doctor
    /// are taken from the consultation, not the payload.</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<List<LabOrder>>> ReplaceByConsultation(
        Guid consultationId, List<LabOrderInput> orders, CancellationToken ct)
    {
        var consult = await db.Consultations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ConsultationId == consultationId, ct);
        if (consult is null) return NotFound(new { message = "Consultation not found." });

        var existing = db.LabOrders.Where(l => l.ConsultationId == consultationId);
        db.LabOrders.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var o in orders)
        {
            if (string.IsNullOrWhiteSpace(o.TestName)) continue;
            db.LabOrders.Add(new LabOrder
            {
                LabOrderId = Guid.NewGuid(),
                ConsultationId = consultationId,
                PatientId = consult.PatientId,
                DoctorId = consult.DoctorId,
                LabTestId = o.LabTestId,
                TestName = o.TestName.Trim(),
                ClinicalIndication = string.IsNullOrWhiteSpace(o.ClinicalIndication) ? null : o.ClinicalIndication.Trim(),
                SpecimenType = string.IsNullOrWhiteSpace(o.SpecimenType) ? null : o.SpecimenType.Trim(),
                Notes = string.IsNullOrWhiteSpace(o.Notes) ? null : o.Notes.Trim(),
                Status = LabOrderStatus.Requested,
                RequestedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(await db.LabOrders.AsNoTracking()
            .Where(l => l.ConsultationId == consultationId).OrderBy(l => l.CreatedAt).ToListAsync(ct));
    }
}
