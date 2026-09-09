using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>clinic_operating_hours / clinic_accepted_payment_methods only. Settings, announcements,
/// audit-logs and the report views are FLAT top-level routes on the frontend (confirmed by grepping
/// every apiService.* call) — see SettingsController, AnnouncementsController, AuditLogsController,
/// ReportsController. Do not add "admin/" prefixed duplicates of those here.</summary>
[ApiController]
[Route("api/admin")]
public class AdminController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet("operating-hours")]
    public async Task<ActionResult<List<ClinicOperatingHour>>> GetOperatingHours(CancellationToken ct) =>
        Ok(await db.ClinicOperatingHours.AsNoTracking().OrderBy(h => h.DayOfWeek).ToListAsync(ct));

    [Authorize(Roles = "Admin")]
    [HttpPut("operating-hours/{dayOfWeek:int}")]
    public async Task<IActionResult> UpdateOperatingHour(int dayOfWeek, ClinicOperatingHour payload, CancellationToken ct)
    {
        var hour = await db.ClinicOperatingHours.SingleOrDefaultAsync(h => h.DayOfWeek == dayOfWeek, ct);
        if (hour is null) return NotFound();

        hour.IsClosed = payload.IsClosed;
        hour.OpenTime = payload.OpenTime;
        hour.CloseTime = payload.CloseTime;

        await db.SaveChangesAsync(ct);
        return Ok(hour);
    }

    /// <summary>Bulk upsert all rows (FE saves the whole week at once).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("operating-hours")]
    public async Task<IActionResult> SetOperatingHours([FromBody] List<ClinicOperatingHour> hours, CancellationToken ct)
    {
        var existing = await db.ClinicOperatingHours.ToListAsync(ct);
        foreach (var h in hours)
        {
            var row = existing.SingleOrDefault(x => x.DayOfWeek == h.DayOfWeek);
            if (row is null)
            {
                db.ClinicOperatingHours.Add(h);
            }
            else
            {
                row.IsClosed = h.IsClosed;
                row.OpenTime = h.OpenTime;
                row.CloseTime = h.CloseTime;
            }
        }
        await db.SaveChangesAsync(ct);
        return Ok(await db.ClinicOperatingHours.AsNoTracking().OrderBy(h => h.DayOfWeek).ToListAsync(ct));
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<List<ClinicAcceptedPaymentMethod>>> GetPaymentMethods(CancellationToken ct) =>
        Ok(await db.ClinicAcceptedPaymentMethods.AsNoTracking().ToListAsync(ct));

    /// <summary>Replace the accepted-methods set (FE sends the full list).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("payment-methods")]
    public async Task<IActionResult> SetPaymentMethods([FromBody] List<string> methods, CancellationToken ct)
    {
        db.ClinicAcceptedPaymentMethods.RemoveRange(db.ClinicAcceptedPaymentMethods);
        foreach (var m in methods.Distinct())
        {
            if (Enum.TryParse<Domain.Enums.PaymentMethod>(m, out var pm))
                db.ClinicAcceptedPaymentMethods.Add(new ClinicAcceptedPaymentMethod { PaymentMethod = pm });
        }
        await db.SaveChangesAsync(ct);
        return Ok(await db.ClinicAcceptedPaymentMethods.AsNoTracking().ToListAsync(ct));
    }
}
