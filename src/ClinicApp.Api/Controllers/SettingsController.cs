using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>Route is flat "api/settings" (NOT "api/admin/settings") — verified against actual FE
/// calls: apiService.get('settings') in app.component.ts (public, on app boot) and home.page.ts,
/// apiService.put('settings', ...) in admin/settings/settings.page.ts.</summary>
[ApiController]
[Route("api/settings")]
public class SettingsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ClinicSetting>> Get(CancellationToken ct)
    {
        var settings = await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct);
        return settings is null ? NotFound() : Ok(settings);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut]
    public async Task<IActionResult> Update(ClinicSetting payload, CancellationToken ct)
    {
        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) return NotFound();

        settings.ClinicName = payload.ClinicName;
        settings.Address = payload.Address;
        settings.ContactNumber = payload.ContactNumber;
        settings.Email = payload.Email;
        settings.Description = payload.Description;
        settings.DefaultPaymentMode = payload.DefaultPaymentMode;
        settings.RefundPolicy = payload.RefundPolicy;
        settings.PrimaryColor = payload.PrimaryColor;
        settings.SecondaryColor = payload.SecondaryColor;
        settings.LogoUrl = payload.LogoUrl;
        settings.FaviconUrl = payload.FaviconUrl;
        settings.WebsiteUrl = payload.WebsiteUrl;
        settings.PrivacyPolicyText = payload.PrivacyPolicyText;
        settings.UpdatedByUserId = CurrentUserId();
        settings.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(settings);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
