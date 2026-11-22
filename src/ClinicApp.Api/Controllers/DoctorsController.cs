//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Validation;
using ClinicApp.Api.Security;
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
public class DoctorsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Doctor>>> GetAll(CancellationToken ct)
    {
        var doctors = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).ToListAsync(ct);
        if (!IsStaffAuthenticated())
            foreach (var d in doctors) RedactForPublicCatalog(d);
        return Ok(doctors);
    }

    /// <summary>§16.2 — paged + searched doctor list. `q` matches name / specialization /
    /// license; `sort` = name | specialization (prefix "-" = descending; default "name").</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<Doctor>>> Search(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? sort = "name", CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Doctors.AsNoTracking().Include(d => d.StaffAccount).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(d =>
                (d.StaffAccount != null && d.StaffAccount.FullName.Contains(s)) ||
                d.Specialization.Contains(s) ||
                (d.LicenseNumber != null && d.LicenseNumber.Contains(s)));
        }

        var desc = sort is not null && sort.StartsWith('-');
        var key = (sort ?? "name").TrimStart('-');
        query = (key, desc) switch
        {
            ("specialization", false) => query.OrderBy(d => d.Specialization),
            ("specialization", true) => query.OrderByDescending(d => d.Specialization),
            (_, true) => query.OrderByDescending(d => d.StaffAccount!.FullName),
            _ => query.OrderBy(d => d.StaffAccount!.FullName),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResult<Doctor> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Doctor>> GetById(Guid id, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).SingleOrDefaultAsync(d => d.DoctorId == id, ct);
        if (doctor is null) return NotFound();
        if (!IsStaffAuthenticated()) RedactForPublicCatalog(doctor);
        return Ok(doctor);
    }

    /// <summary>The logged-in doctor's own row (doctor-dashboard.page.ts, doctor-schedule.page.ts).</summary>
    [Authorize(Roles = "Doctor")]
    [HttpGet("me")]
    public async Task<ActionResult<Doctor>> GetMine(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var doctor = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount)
            .SingleOrDefaultAsync(d => d.StaffAccount != null && d.StaffAccount.UserId == userId, ct);

        return doctor is null ? NotFound() : Ok(doctor);
    }

    /// <summary>Admin doctor management list — same shape as GetAll, kept as a distinct route
    /// because the FE calls doctors/admin specifically from the admin portal.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public Task<ActionResult<List<Doctor>>> GetAllForAdmin(CancellationToken ct) => GetAll(ct);


    /// <summary>Public booking wizard may list doctors, but license / PTR / S2 and staff
    /// contact details stay staff-authenticated only.</summary>
    private static void RedactForPublicCatalog(Doctor d)
    {
        d.LicenseNumber = null;
        d.PtrNumber = null;
        d.S2Number = null;
        if (d.StaffAccount is not null)
        {
            d.StaffAccount.Email = "";
            d.StaffAccount.ContactNumber = null;
        }
    }

    private bool IsStaffAuthenticated() =>
        User.Identity?.IsAuthenticated == true &&
        (User.IsInRole("Admin") || User.IsInRole("Staff") || User.IsInRole("Doctor"));

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    //JJQR - AI (21 Nov 2026) desktop: a doctor edits their own bio only (fee, slots, limits, license/PTR/S2 stay Admin-only on PUT {id})
    //JJQR - AI (Nov 22, 2026) - the doctor also keeps their own License / PTR / S2 numbers (printed on prescriptions and certificates); null = leave as is, "" = clear //JJQR - END
    //JJQR - AI (Nov 22, 2026) - owner: the doctor may change their OWN professional / consultation fee (ConsultationFee; null = leave as is). Only their own row can be reached (this is /me); name, specialization, slots and limits stay Admin-only. Applies to NEW bookings only.
    public sealed record UpdateMyBioRequest(string? Bio, string? LicenseNumber = null, string? PtrNumber = null, string? S2Number = null, decimal? ConsultationFee = null);
    //JJQR - END

    static string? Pick(string? sent, string? current) => sent is null ? current : string.IsNullOrWhiteSpace(sent) ? null : sent.Trim();

    [Authorize(Roles = "Doctor")]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMine(UpdateMyBioRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var doctor = await db.Doctors.Include(d => d.StaffAccount)
            .SingleOrDefaultAsync(d => d.StaffAccount != null && d.StaffAccount.UserId == userId, ct);
        if (doctor is null) return NotFound();

        var bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        // Reuse Rules.Doctor on a copy (existing values + the new bio) so the bio limit is the same one Admin edits use.
        var probe = new Doctor
        {
            Specialization = doctor.Specialization, ConsultationFee = request.ConsultationFee ?? doctor.ConsultationFee, Bio = bio,
            LicenseNumber = Pick(request.LicenseNumber, doctor.LicenseNumber), PtrNumber = Pick(request.PtrNumber, doctor.PtrNumber), S2Number = Pick(request.S2Number, doctor.S2Number),
            SlotDurationMinutes = doctor.SlotDurationMinutes, SlotCapacity = doctor.SlotCapacity, DailyPatientLimit = doctor.DailyPatientLimit,
        };
        if (Rules.Doctor(probe).Result() is { } invalid) return invalid;

        doctor.Bio = bio;
        doctor.LicenseNumber = probe.LicenseNumber; doctor.PtrNumber = probe.PtrNumber; doctor.S2Number = probe.S2Number;
        //JJQR - AI (Nov 22, 2026) - own fee change: audited (old and new amount, who), no patient data
        if (request.ConsultationFee is { } newFee && newFee != doctor.ConsultationFee)
        {
            ClinicApp.Api.Auditing.AuditLogWriter.Add(db, ClinicApp.Domain.Enums.AuditEntityType.Doctor, doctor.DoctorId, "OwnFeeChanged", userId, $"consultation_fee {doctor.ConsultationFee:0.##} -> {newFee:0.##}");
            doctor.ConsultationFee = newFee;
        }
        //JJQR - END
        await db.SaveChangesAsync(ct);
        return Ok(doctor);
    }
    //JJQR - END

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Doctor payload, CancellationToken ct)
    {
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.DoctorId == id, ct);
        if (doctor is null) return NotFound();
        if (Rules.Doctor(payload).Result() is { } invalid) return invalid; // JJQR - AI (Nov 20, 2026) - B-31

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
        // A doctor edits only their own schedule; Admin may edit any.
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(id)) return Forbid();
        // Conflict key: (doctor_id, day_of_week) — contract §4.
        var existing = await db.DoctorSchedules.SingleOrDefaultAsync(s => s.DoctorId == id && s.DayOfWeek == payload.DayOfWeek, ct);
        if (existing is null)
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            if (await ClientIds.TakenAsync<DoctorSchedule>(db, payload.Id, ct)) return ClientIds.Conflict();
            payload.Id = ClientIds.Use(payload.Id);
            //JJQR - END
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
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(id)) return Forbid();
        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (await ClientIds.TakenAsync<DoctorBlockedDate>(db, payload.Id, ct)) return ClientIds.Conflict();
        payload.Id = ClientIds.Use(payload.Id);
        //JJQR - END
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
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(row.DoctorId)) return Forbid();
        db.DoctorBlockedDates.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── doctor_day_statuses ─────────────────────────────────────────────
    [HttpGet("{id:guid}/day-status")]
    public async Task<ActionResult<DoctorDayStatus?>> GetDayStatus(Guid id, [FromQuery] DateOnly date, CancellationToken ct) =>
        Ok(await db.DoctorDayStatuses.AsNoTracking().SingleOrDefaultAsync(s => s.DoctorId == id && s.StatusDate == date, ct));

    /// <summary>All doctors' status for a date (staff/doctor-status board).</summary>
    [HttpGet("day-statuses")]
    public async Task<ActionResult<List<DoctorDayStatus>>> GetDayStatuses([FromQuery] DateOnly date, CancellationToken ct) =>
        Ok(await db.DoctorDayStatuses.AsNoTracking().Where(s => s.StatusDate == date).ToListAsync(ct));

    [Authorize(Roles = "Admin,Doctor,Staff")]
    [HttpPut("{id:guid}/day-status")]
    public async Task<IActionResult> UpsertDayStatus(Guid id, DoctorDayStatus payload, CancellationToken ct)
    {
        // Front desk (Staff) and Admin may set any doctor's day status; a Doctor only their own.
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor && !actor.ActsAsDoctor(id)) return Forbid();
        // Conflict key: (doctor_id, status_date) — contract §4.
        var now = DateTimeOffset.UtcNow;
        var existing = await db.DoctorDayStatuses.SingleOrDefaultAsync(s => s.DoctorId == id && s.StatusDate == payload.StatusDate, ct);
        if (existing is null)
        {
            payload.Id = Guid.NewGuid();
            payload.DoctorId = id;
            payload.CreatedAt = now;
            payload.UpdatedAt = now;
            db.DoctorDayStatuses.Add(payload);
        }
        else
        {
            existing.Status = payload.Status;
            existing.RunningLateMinutes = payload.RunningLateMinutes;
            existing.UpdatedAt = now;
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
