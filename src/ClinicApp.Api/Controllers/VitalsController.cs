using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record VitalReadingInput(Guid TemplateId, string Value);

/// <summary>patient_vital_readings (contract §4). Upsert conflict key is
/// (booking_id, template_id). Contract §16.1: vitals are a shared capability —
/// any staff-like user (Staff / Doctor / Admin) may write them.</summary>
[ApiController]
[Authorize]
[Route("api/vitals")]
public class VitalsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PatientVitalReading>>> GetAll(
        [FromQuery] Guid? bookingId, [FromQuery] Guid? patientId, CancellationToken ct)
    {
        var q = db.PatientVitalReadings.AsNoTracking().AsQueryable();
        if (bookingId is not null) q = q.Where(r => r.BookingId == bookingId);
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        return Ok(await q.ToListAsync(ct));
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

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.Date);
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
                    Id = Guid.NewGuid(),
                    BookingId = bookingId,
                    PatientId = booking.PatientId,
                    TemplateId = input.TemplateId,
                    Value = input.Value,
                    RecordedAt = today,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                row.Value = input.Value;
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(await db.PatientVitalReadings.AsNoTracking().Where(r => r.BookingId == bookingId).ToListAsync(ct));
    }
}
