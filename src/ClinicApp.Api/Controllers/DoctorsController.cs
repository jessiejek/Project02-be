using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>snake_case wire format (contract §4/§6) — no [Authorize] on GETs the public booking
/// flow needs (doctor list/schedule are shown pre-login).</summary>
[ApiController]
[Route("api/doctors")]
public class DoctorsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Doctor>>> GetAll(CancellationToken ct)
    {
        var doctors = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).ToListAsync(ct);
        return Ok(doctors);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Doctor>> GetById(Guid id, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).SingleOrDefaultAsync(d => d.DoctorId == id, ct);
        return doctor is null ? NotFound() : Ok(doctor);
    }

    /// <summary>The logged-in doctor's own row (doctor-dashboard.page.ts, doctor-schedule.page.ts).</summary>
    [Authorize(Roles = "Doctor")]
    [HttpGet("me")]
    public async Task<ActionResult<Doctor>> GetMine(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var doctor = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount)
            .SingleOrDefaultAsync(d => d.StaffAccount.UserId == userId, ct);

        return doctor is null ? NotFound() : Ok(doctor);
    }

    /// <summary>Admin doctor management list — same shape as GetAll, kept as a distinct route
    /// because the FE calls doctors/admin specifically from the admin portal.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public Task<ActionResult<List<Doctor>>> GetAllForAdmin(CancellationToken ct) => GetAll(ct);

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Doctor payload, CancellationToken ct)
    {
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.DoctorId == id, ct);
        if (doctor is null) return NotFound();

        doctor.Specialization = payload.Specialization;
        doctor.ConsultationFee = payload.ConsultationFee;
        doctor.Bio = payload.Bio;
        doctor.LicenseNumber = payload.LicenseNumber;
        doctor.PtrNumber = payload.PtrNumber;
        doctor.S2Number = payload.S2Number;
        doctor.SlotDurationMinutes = payload.SlotDurationMinutes;
        doctor.SlotCapacity = payload.SlotCapacity;
        doctor.DailyPatientLimit = payload.DailyPatientLimit;

        await db.SaveChangesAsync(ct);
        return Ok(doctor);
    }

    // ── doctor_schedules ────────────────────────────────────────────────
    [HttpGet("{id:guid}/schedules")]
    public async Task<ActionResult<List<DoctorSchedule>>> GetSchedules(Guid id, CancellationToken ct) =>
        Ok(await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == id).ToListAsync(ct));

    [Authorize(Roles = "Admin,Doctor")]
    [HttpPut("{id:guid}/schedules")]
    public async Task<IActionResult> UpsertSchedule(Guid id, DoctorSchedule payload, CancellationToken ct)
    {
        // Conflict key: (doctor_id, day_of_week) — contract §4.
        var existing = await db.DoctorSchedules.SingleOrDefaultAsync(s => s.DoctorId == id && s.DayOfWeek == payload.DayOfWeek, ct);
        if (existing is null)
        {
            payload.Id = Guid.NewGuid();
            payload.DoctorId = id;
            payload.CreatedAt = DateTimeOffset.UtcNow;
            db.DoctorSchedules.Add(payload);
        }
        else
        {
            existing.IsActive = payload.IsActive;
            existing.StartTime = payload.StartTime;
            existing.EndTime = payload.EndTime;
        }

        await db.SaveChangesAsync(ct);
        return Ok(existing ?? payload);
    }

    // ── doctor_blocked_dates ────────────────────────────────────────────
    [HttpGet("{id:guid}/blocked-dates")]
    public async Task<ActionResult<List<DoctorBlockedDate>>> GetBlockedDates(Guid id, CancellationToken ct) =>
        Ok(await db.DoctorBlockedDates.AsNoTracking().Where(b => b.DoctorId == id).ToListAsync(ct));

    [Authorize(Roles = "Admin,Doctor")]
    [HttpPost("{id:guid}/blocked-dates")]
    public async Task<IActionResult> AddBlockedDate(Guid id, DoctorBlockedDate payload, CancellationToken ct)
    {
        payload.Id = Guid.NewGuid();
        payload.DoctorId = id;
        payload.CreatedAt = DateTimeOffset.UtcNow;
        db.DoctorBlockedDates.Add(payload);
        await db.SaveChangesAsync(ct);
        return Ok(payload);
    }

    [Authorize(Roles = "Admin,Doctor")]
    [HttpDelete("blocked-dates/{blockedDateId:guid}")]
    public async Task<IActionResult> RemoveBlockedDate(Guid blockedDateId, CancellationToken ct)
    {
        var row = await db.DoctorBlockedDates.FindAsync([blockedDateId], ct);
        if (row is null) return NotFound();
        db.DoctorBlockedDates.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── doctor_day_statuses ─────────────────────────────────────────────
    [HttpGet("{id:guid}/day-status")]
    public async Task<ActionResult<DoctorDayStatus?>> GetDayStatus(Guid id, [FromQuery] DateOnly date, CancellationToken ct) =>
        Ok(await db.DoctorDayStatuses.AsNoTracking().SingleOrDefaultAsync(s => s.DoctorId == id && s.StatusDate == date, ct));

    [Authorize(Roles = "Admin,Doctor,Staff")]
    [HttpPut("{id:guid}/day-status")]
    public async Task<IActionResult> UpsertDayStatus(Guid id, DoctorDayStatus payload, CancellationToken ct)
    {
        // Conflict key: (doctor_id, status_date) — contract §4.
        var existing = await db.DoctorDayStatuses.SingleOrDefaultAsync(s => s.DoctorId == id && s.StatusDate == payload.StatusDate, ct);
        if (existing is null)
        {
            payload.Id = Guid.NewGuid();
            payload.DoctorId = id;
            payload.CreatedAt = DateTimeOffset.UtcNow;
            db.DoctorDayStatuses.Add(payload);
        }
        else
        {
            existing.Status = payload.Status;
            existing.RunningLateMinutes = payload.RunningLateMinutes;
        }

        await db.SaveChangesAsync(ct);
        return Ok(existing ?? payload);
    }

    // ── doctor_services ─────────────────────────────────────────────────
    [HttpGet("{id:guid}/services")]
    public async Task<ActionResult<List<DoctorService>>> GetServices(Guid id, CancellationToken ct) =>
        Ok(await db.DoctorServices.AsNoTracking().Include(ds => ds.Service).Where(ds => ds.DoctorId == id).ToListAsync(ct));

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}/services")]
    public async Task<IActionResult> SetServices(Guid id, [FromBody] List<Guid> serviceIds, CancellationToken ct)
    {
        var existing = db.DoctorServices.Where(ds => ds.DoctorId == id);
        db.DoctorServices.RemoveRange(existing);

        foreach (var serviceId in serviceIds)
        {
            db.DoctorServices.Add(new DoctorService { DoctorId = id, ServiceId = serviceId, DurationMinutes = 30, CreatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
