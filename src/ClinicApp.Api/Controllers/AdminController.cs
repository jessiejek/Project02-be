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

    [HttpGet("payment-methods")]
    public async Task<ActionResult<List<ClinicAcceptedPaymentMethod>>> GetPaymentMethods(CancellationToken ct) =>
        Ok(await db.ClinicAcceptedPaymentMethods.AsNoTracking().ToListAsync(ct));
}
