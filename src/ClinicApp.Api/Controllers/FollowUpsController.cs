using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record UpsertFollowUpRequest(
    Guid PatientId, Guid DoctorId, DateOnly FollowUpDate,
    string? Reason, string? Instructions, bool ReminderEnabled, FollowUpStatus Status);

/// <summary>follow_ups (contract §4). Upsert conflict key is consultation_id (unique).</summary>
[ApiController]
[Authorize]
[Route("api/follow-ups")]
public class FollowUpsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FollowUp>>> GetAll(
        [FromQuery] Guid? patientId, [FromQuery] Guid? doctorId, [FromQuery] Guid? consultationId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();

        var q = db.FollowUps.AsNoTracking().AsQueryable();
        if (patientId is not null) q = q.Where(f => f.PatientId == patientId);
        if (doctorId is not null) q = q.Where(f => f.DoctorId == doctorId);
        if (consultationId is not null) q = q.Where(f => f.ConsultationId == consultationId);
        return Ok(await q.OrderBy(f => f.FollowUpDate).ToListAsync(ct));
    }

    [HttpGet("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<FollowUp>> GetByConsultation(Guid consultationId, CancellationToken ct)
    {
        var f = await db.FollowUps.AsNoTracking().SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        if (f is null) return NotFound();
        return (await actors.ResolveAsync(User, ct)).CanAccessPatient(f.PatientId) ? Ok(f) : NotFound();
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<FollowUp>> UpsertByConsultation(Guid consultationId, UpsertFollowUpRequest req, CancellationToken ct)
    {
        // Patient / doctor come from the consultation; a doctor only writes their own.
        var consult = await db.Consultations.AsNoTracking().SingleOrDefaultAsync(c => c.ConsultationId == consultationId, ct);
        if (consult is null) return NotFound(new { message = "Consultation not found." });
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(consult.DoctorId)) return Forbid();
        if (req.PatientId != consult.PatientId || req.DoctorId != consult.DoctorId)
            return BadRequest(new { message = "Patient / doctor do not match the consultation." });

        var now = DateTimeOffset.UtcNow;
        var f = await db.FollowUps.SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        if (f is null)
        {
            f = new FollowUp { Id = Guid.NewGuid(), ConsultationId = consultationId, CreatedAt = now };
            db.FollowUps.Add(f);
        }
        f.PatientId = req.PatientId;
        f.DoctorId = req.DoctorId;
        f.FollowUpDate = req.FollowUpDate;
        f.Reason = req.Reason;
        f.Instructions = req.Instructions;
        f.ReminderEnabled = req.ReminderEnabled;
        f.Status = req.Status;
        f.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return Ok(f);
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpDelete("by-consultation/{consultationId:guid}")]
    public async Task<IActionResult> DeleteByConsultation(Guid consultationId, CancellationToken ct)
    {
        var f = await db.FollowUps.SingleOrDefaultAsync(x => x.ConsultationId == consultationId, ct);
        if (f is null) return NotFound();
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(f.DoctorId)) return Forbid();
        db.FollowUps.Remove(f);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
