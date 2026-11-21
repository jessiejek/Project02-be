using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>Flat "api/reports/*" — confirmed by FE calls (reports/daily-booking-summary,
/// reports/pending-follow-ups, reports/unpaid-completed-visits). Maps to the 4 keyless SQL views
/// from contract §5.</summary>
[ApiController]
[Authorize] // authenticated; per-endpoint role gates below
[Route("api/reports")]
public class ReportsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    // Multiple [Authorize] attributes are AND-ed, so a wider list here would
    // still be capped by a class-level Roles list — hence the class attr has none
    // and every endpoint states its own.
    private const string StaffReports = "Admin,Staff,Doctor";

    /// <summary>Patient-facing too — ratings are shown on the patient doctor
    /// cards / doctor profile. The other 3 report views stay staff-only.</summary>
    [HttpGet("doctor-ratings")]
    [Authorize(Roles = StaffReports + ",Patient")]
    public async Task<ActionResult<List<VDoctorRating>>> GetDoctorRatings(CancellationToken ct) =>
        Ok(await db.VDoctorRatings.AsNoTracking().ToListAsync(ct));

    //JJQR - AI (Nov 19, 2026) - Doctor callers only get THEIR rows. Staff / Admin keep the clinic-wide views untouched.
    [HttpGet("daily-booking-summary")]
    [Authorize(Roles = StaffReports)]
    public async Task<ActionResult<List<VDailyBookingSummary>>> GetDailyBookingSummary(CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsDoctor) return Ok(await db.VDailyBookingSummaries.AsNoTracking().ToListAsync(ct));
        if (actor.StaffId is not { } me) return Forbid();

        // Same columns as v_daily_booking_summary, computed from this doctor's bookings (no migration).
        var rows = await db.Bookings.AsNoTracking().Where(b => b.DoctorId == me)
            .Select(b => new
            {
                b.AppointmentDate, b.Status,
                PayStatus = b.Payment == null ? (PaymentStatus?)null : b.Payment.Status,
                Amount = b.Payment == null ? (decimal?)null : b.Payment.Amount,
                Received = b.Payment == null ? null : b.Payment.AmountReceived
            }).ToListAsync(ct);
        return Ok(rows.GroupBy(r => r.AppointmentDate).OrderBy(g => g.Key).Select(g => new VDailyBookingSummary
        {
            AppointmentDate = g.Key,
            TotalBookings = g.Count(),
            CompletedCount = g.Count(r => r.Status == BookingStatus.Completed),
            PaidCount = g.Count(r => r.PayStatus == PaymentStatus.Paid),
            //JJQR - AI (Nov 21, 2026) - a cancelled / no-show / expired booking owes nothing, so its leftover "Unpaid" payment row is not counted (same rule as v_daily_booking_summary)
            UnpaidCount = g.Count(r => r.PayStatus == PaymentStatus.Unpaid && r.Status != BookingStatus.Cancelled && r.Status != BookingStatus.NoShow && r.Status != BookingStatus.Expired && r.Status != BookingStatus.Rescheduled),
            //JJQR - END
            NoShowCount = g.Count(r => r.Status == BookingStatus.NoShow),
            Revenue = g.Where(r => r.PayStatus == PaymentStatus.Paid).Sum(r => r.Received ?? r.Amount ?? 0m)
        }).ToList());
    }

    [HttpGet("unpaid-completed-visits")]
    [Authorize(Roles = StaffReports)]
    public async Task<ActionResult<List<VUnpaidCompletedVisit>>> GetUnpaidCompletedVisits(CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        var q = db.VUnpaidCompletedVisits.AsNoTracking().AsQueryable();
        if (actor.IsDoctor)
        {
            if (actor.StaffId is not { } me) return Forbid();
            q = q.Where(v => v.DoctorId == me);
        }
        return Ok(await q.ToListAsync(ct));
    }

    [HttpGet("pending-follow-ups")]
    [Authorize(Roles = StaffReports)]
    public async Task<ActionResult<List<VPendingFollowUp>>> GetPendingFollowUps(CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        var q = db.VPendingFollowUps.AsNoTracking().AsQueryable();
        if (actor.IsDoctor)
        {
            if (actor.StaffId is not { } me) return Forbid();
            q = q.Where(v => v.DoctorId == me);
        }
        return Ok(await q.ToListAsync(ct));
    }
    //JJQR - END

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
