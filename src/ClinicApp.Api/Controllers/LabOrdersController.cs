//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Validation;
using ClinicApp.Api.Security;
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
    string? Notes,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? LabOrderId = null
    //JJQR - END
    );

/// <summary>lab_orders (§16.8 Form 3). A consultation's lab request is a
/// replace-all set keyed on consultation_id, same shape as
/// consultation_diagnoses.</summary>
[ApiController]
[Authorize]
[Route("api/lab-orders")]
public class LabOrdersController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LabOrder>>> GetAll(
        [FromQuery] Guid? consultationId, [FromQuery] Guid? patientId, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();

        var q = db.LabOrders.AsNoTracking().AsQueryable();
        //JJQR - AI (Nov 19, 2026) - a Doctor only ever lists their OWN lab orders (Staff / Admin unchanged)
        if (actor.IsDoctor)
        {
            if (actor.StaffId is not { } ownDoctor) return Forbid();
            q = q.Where(l => l.DoctorId == ownDoctor);
        }
        //JJQR - END
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
    public async Task<ActionResult<List<LabOrder>>> GetByConsultation(Guid consultationId, CancellationToken ct)
    {
        var owner = await db.Consultations.AsNoTracking().Where(c => c.ConsultationId == consultationId)
            .Select(c => new { c.PatientId, c.DoctorId }).SingleOrDefaultAsync(ct);
        // Unknown id and someone else's id look identical to a patient (empty list), like RLS.
        var labActor = await actors.ResolveAsync(User, ct);
        if (owner is null || !labActor.CanAccessPatient(owner.PatientId) || (labActor.IsDoctor && labActor.StaffId != owner.DoctorId)) return Ok(new List<LabOrder>()); //JJQR - AI (Nov 22, 2026) - QA high #7: a doctor sees only their own lab orders //JJQR - END
        return Ok(await db.LabOrders.AsNoTracking()
            .Where(l => l.ConsultationId == consultationId)
            .OrderBy(l => l.CreatedAt).ToListAsync(ct));
    }

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<List<LabOrder>>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var owned = await db.Consultations.AsNoTracking().Where(c => c.BookingId == bookingId)
            .Select(c => new { c.ConsultationId, c.PatientId, c.DoctorId }).SingleOrDefaultAsync(ct);
        var labBookingActor = await actors.ResolveAsync(User, ct);
        if (owned is null || !labBookingActor.CanAccessPatient(owned.PatientId) || (labBookingActor.IsDoctor && labBookingActor.StaffId != owned.DoctorId)) return Ok(new List<LabOrder>()); //JJQR - AI (Nov 22, 2026) - QA high #7 //JJQR - END
        var cid = owned.ConsultationId;
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
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(consult.DoctorId)) return Forbid();

        // JJQR - AI (Nov 20, 2026) - B-01: every row needs a test name (the UI drops blank rows before sending)
        foreach (var o in orders)
            if (Rules.LabOrder(o).Result() is { } invalid) return invalid;
        // JJQR - END
        var existing = db.LabOrders.Where(l => l.ConsultationId == consultationId);
        db.LabOrders.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        foreach (var o in orders)
        {
            if (string.IsNullOrWhiteSpace(o.TestName)) continue;
            db.LabOrders.Add(new LabOrder
            {
                //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
                LabOrderId = await ClientIds.FreshAsync<LabOrder>(db, o.LabOrderId, ct),
                //JJQR - END
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
