using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>Flat "api/reports/*" — confirmed by FE calls (reports/daily-booking-summary,
/// reports/pending-follow-ups, reports/unpaid-completed-visits). Maps to the 4 keyless SQL views
/// from contract §5.</summary>
[ApiController]
[Authorize(Roles = "Admin,Staff,Doctor")]
[Route("api/reports")]
public class ReportsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet("doctor-ratings")]
    public async Task<ActionResult<List<VDoctorRating>>> GetDoctorRatings(CancellationToken ct) =>
        Ok(await db.VDoctorRatings.AsNoTracking().ToListAsync(ct));

    [HttpGet("daily-booking-summary")]
    public async Task<ActionResult<List<VDailyBookingSummary>>> GetDailyBookingSummary(CancellationToken ct) =>
        Ok(await db.VDailyBookingSummaries.AsNoTracking().ToListAsync(ct));

    [HttpGet("unpaid-completed-visits")]
    public async Task<ActionResult<List<VUnpaidCompletedVisit>>> GetUnpaidCompletedVisits(CancellationToken ct) =>
        Ok(await db.VUnpaidCompletedVisits.AsNoTracking().ToListAsync(ct));

    [HttpGet("pending-follow-ups")]
    public async Task<ActionResult<List<VPendingFollowUp>>> GetPendingFollowUps(CancellationToken ct) =>
        Ok(await db.VPendingFollowUps.AsNoTracking().ToListAsync(ct));

    /// <summary>§16.9 — monthly earnings for the single doctor. A Doctor caller
    /// only ever sees their own rows; Admin may pass ?doctorId= to scope, or omit
    /// it for all doctors.</summary>
    [HttpGet("doctor-earnings")]
    [Authorize(Roles = "Doctor,Admin")]
    public async Task<ActionResult<List<VDoctorEarnings>>> GetDoctorEarnings(
        [FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var q = db.VDoctorEarnings.AsNoTracking().AsQueryable();

        if (User.IsInRole("Doctor") && !User.IsInRole("Admin"))
        {
            var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(sub, out var uid)) return Forbid();
            var myDoctorId = await db.StaffAccounts
                .Where(s => s.UserId == uid)
                .Select(s => (Guid?)s.StaffId)
                .SingleOrDefaultAsync(ct);
            if (myDoctorId is null) return Forbid();
            q = q.Where(e => e.DoctorId == myDoctorId);
        }
        else if (doctorId is not null)
        {
            q = q.Where(e => e.DoctorId == doctorId);
        }

        return Ok(await q.OrderByDescending(e => e.Period).ToListAsync(ct));
    }
}
