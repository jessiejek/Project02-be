using ClinicApp.Api.Validation;
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

        // JJQR - AI (Nov 20, 2026) - B-01 / B-34 / B-36 / B-37: validate settings server-side, and really store the consent version
        var check = new Check()
            .Required("clinic_name", "Clinic name", payload.ClinicName, 120)
            .Required("address", "Address", payload.Address, 300)
            .Phone("contact_number", "Contact number", payload.ContactNumber)
            .Email("email", "Email", payload.Email)
            .Optional("description", "Description", payload.Description, 2000)
            .Optional("refund_policy", "Refund policy", payload.RefundPolicy, 4000)
            .Optional("privacy_policy_text", "Privacy policy", payload.PrivacyPolicyText, 20000)
            .Hex("primary_color", "Primary colour", payload.PrimaryColor)
            .Hex("secondary_color", "Secondary colour", payload.SecondaryColor)
            .Url("logo_url", "Logo address", payload.LogoUrl)
            .Url("favicon_url", "Favicon address", payload.FaviconUrl)
            .Url("website_url", "Website address", payload.WebsiteUrl)
            .Money("fee_consultation", "Consultation fee", payload.FeeConsultation)
            .Money("fee_follow_up", "Follow-up fee", payload.FeeFollowUp)
            .Money("fee_senior_pwd", "Senior/PWD fee", payload.FeeSeniorPwd)
            .Money("fee_med_cert", "Medical certificate fee", payload.FeeMedCert)
            .Range("discount_pct", "Discount", payload.DiscountPct, 0m, 1m);
        if (!Enum.IsDefined(payload.DefaultPaymentMode)) check.Fail("default_payment_mode", "Choose a valid default payment mode.");
        // The consent version only ever moves forward, one published version at a time (an old client PUTting a stale value must not roll it back).
        if (payload.ConsentVersion < settings.ConsentVersion || payload.ConsentVersion > settings.ConsentVersion + 1)
            check.Fail("consent_version", "Consent version can only stay the same or go up by one.");
        if (check.Result() is { } bad) return bad;
        // JJQR - END

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
        // §16.6 fee schedule.
        settings.FeeConsultation = payload.FeeConsultation;
        settings.FeeFollowUp = payload.FeeFollowUp;
        settings.FeeSeniorPwd = payload.FeeSeniorPwd;
        settings.FeeMedCert = payload.FeeMedCert;
        settings.DiscountPct = payload.DiscountPct;
        // JJQR - AI (Nov 20, 2026) - B-37: publishing a new consent version is stored (it used to be dropped here)
        if (payload.ConsentVersion > settings.ConsentVersion)
        {
            settings.ConsentVersion = payload.ConsentVersion;
            ClinicApp.Api.Auditing.AuditLogWriter.Add(db, ClinicApp.Domain.Enums.AuditEntityType.Settings, Guid.Empty, "Consent version published", CurrentUserId(), $"Consent version → {settings.ConsentVersion}");
        }
        // JJQR - END
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
