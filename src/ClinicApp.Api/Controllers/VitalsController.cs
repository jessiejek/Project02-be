//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Validation;
using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record VitalReadingInput(Guid TemplateId, string Value,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? Id = null
    //JJQR - END
    );

/// <summary>patient_vital_readings (contract §4). Upsert conflict key is
/// (booking_id, template_id). Contract §16.1: vitals are a shared capability —
/// any staff-like user (Staff / Doctor / Admin) may write them.</summary>
[ApiController]
[Authorize]
[Route("api/vitals")]
public class VitalsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PatientVitalReading>>> GetAll(
        [FromQuery] Guid? bookingId, [FromQuery] Guid? patientId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();

        var q = db.PatientVitalReadings.AsNoTracking().AsQueryable();
        if (bookingId is not null) q = q.Where(r => r.BookingId == bookingId);
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        var list = await q.ToListAsync(ct);
        foreach (var pid in list.Select(r => r.PatientId).Distinct().Take(20)) await AccessLog.RecordAsync(db, actor, pid, "Vitals", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(list);
    }

    /// <summary>Bulk upsert for one booking. An empty <c>value</c> deletes that
    /// reading (matches the consultation page clearing a field).</summary>
    [Authorize(Roles = "Staff,Doctor,Admin")]
    [HttpPut("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<List<PatientVitalReading>>> UpsertByBooking(
        Guid bookingId, [FromBody] List<VitalReadingInput> readings, CancellationToken ct)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return NotFound(new { message = "Booking not found." });

        // JJQR - AI (Nov 20, 2026) - B-01: refuse oversized values and template ids that do not exist
        if (Rules.VitalReadings(readings).Result() is { } invalid) return invalid;
        var knownTemplates = await db.VitalFieldTemplates.AsNoTracking().Select(t => t.TemplateId).ToListAsync(ct);
        if (readings.Any(r => !knownTemplates.Contains(r.TemplateId)))
            return BadRequest(new { message = "One of the vital fields is not recognised." });
        // JJQR - END
        var now = DateTimeOffset.UtcNow;
        var today = ClinicApp.Domain.ClinicClock.Today; // clinic (Manila) date, not the UTC date
        var uid = CurrentUserId();
        var existing = await db.PatientVitalReadings.Where(r => r.BookingId == bookingId).ToListAsync(ct);

        foreach (var input in readings)
        {
            var row = existing.SingleOrDefault(r => r.TemplateId == input.TemplateId);
            if (string.IsNullOrWhiteSpace(input.Value))
            {
                if (row is not null) db.PatientVitalReadings.Remove(row);
                continue;
            }
            if (row is null)
            {
                db.PatientVitalReadings.Add(new PatientVitalReading
                {
                    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
                    Id = await ClientIds.FreshAsync<PatientVitalReading>(db, input.Id, ct),
                    //JJQR - END
                    BookingId = bookingId,
                    PatientId = booking.PatientId,
                    TemplateId = input.TemplateId,
                    Value = input.Value,
                    RecordedAt = today,
                    RecordedByUserId = uid,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                row.Value = input.Value;
                row.RecordedByUserId = uid;
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(await db.PatientVitalReadings.AsNoTracking().Where(r => r.BookingId == bookingId).ToListAsync(ct));
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
