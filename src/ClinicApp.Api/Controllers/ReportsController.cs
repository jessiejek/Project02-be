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
}
