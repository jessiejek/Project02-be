// JJQR - AI (Nov 20, 2026) - B-01 / B-34 / B-36 / B-37: settings validation and the consent version really publishing
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class SettingsRulesTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<HttpClient> AdminAsync()
    {
        await WithDb(async db =>
        {
            if (!await db.ClinicSettings.AnyAsync(s => s.Id == 1))
            {
                db.ClinicSettings.Add(new ClinicSetting { Id = 1, ClinicName = "Test Clinic", Address = "Cebu", DefaultPaymentMode = PaymentMode.PayAtClinic, UpdatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            return 0;
        });
        var admin = await TestHelpers.NewStaffLoginAsync(F, UserRole.Admin);
        return F.ClientFor(admin.UserId, "Admin");
    }

    private async Task<ClinicSetting> Current() => await WithDb(db => db.ClinicSettings.AsNoTracking().SingleAsync(s => s.Id == 1));

    private static StringContent Body(ClinicSetting s, Action<Dictionary<string, object?>>? tweak = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["clinic_name"] = s.ClinicName, ["address"] = s.Address, ["contact_number"] = s.ContactNumber, ["email"] = s.Email,
            ["description"] = s.Description, ["default_payment_mode"] = s.DefaultPaymentMode.ToString(), ["refund_policy"] = s.RefundPolicy,
            ["consent_version"] = s.ConsentVersion, ["fee_consultation"] = s.FeeConsultation, ["fee_follow_up"] = s.FeeFollowUp,
            ["fee_senior_pwd"] = s.FeeSeniorPwd, ["fee_med_cert"] = s.FeeMedCert, ["discount_pct"] = s.DiscountPct
        };
        tweak?.Invoke(d);
        return ApiFactory.Body(d);
    }

    [Theory]
    [InlineData("clinic_name", "")]
    [InlineData("clinic_name", "   ")]
    [InlineData("address", "")]
    [InlineData("fee_consultation", -1)]
    [InlineData("fee_follow_up", -0.01)]
    [InlineData("fee_med_cert", 99999999)]
    [InlineData("discount_pct", 1.5)]
    [InlineData("email", "not-an-email")]
    [InlineData("contact_number", "abc")]
    public async Task Bad_settings_are_refused_and_nothing_changes(string field, object value)
    {
        using var c = await AdminAsync();
        var before = await Current();
        var res = await c.PutAsync("/api/settings", Body(before, d => d[field] = value));
        await ShouldBe(HttpStatusCode.BadRequest, res);
        var after = await Current();
        Assert.Equal(before.ClinicName, after.ClinicName);
        Assert.Equal(before.Address, after.Address);
        Assert.Equal(before.FeeConsultation, after.FeeConsultation);
    }

    [Fact]
    public async Task Valid_settings_are_saved_and_the_name_and_address_come_back()
    {
        using var c = await AdminAsync();
        var before = await Current();
        await ShouldBe(HttpStatusCode.OK, await c.PutAsync("/api/settings", Body(before, d => { d["clinic_name"] = "Gavino Clinic QA"; d["address"] = "1 Test St"; })));
        var json = await JsonOf(await c.GetAsync("/api/settings"));
        Assert.Equal("Gavino Clinic QA", json.GetProperty("clinic_name").GetString());
        Assert.Equal("1 Test St", json.GetProperty("address").GetString());
        await ShouldBe(HttpStatusCode.OK, await c.PutAsync("/api/settings", Body(before)));
    }

    [Fact]
    public async Task Publishing_a_consent_version_is_stored_and_cannot_be_rolled_back_or_skipped()
    {
        using var c = await AdminAsync();
        var before = await Current();
        await ShouldBe(HttpStatusCode.OK, await c.PutAsync("/api/settings", Body(before, d => d["consent_version"] = before.ConsentVersion + 1)));
        Assert.Equal(before.ConsentVersion + 1, (await Current()).ConsentVersion);
        var json = await JsonOf(await c.GetAsync("/api/settings"));
        Assert.Equal(before.ConsentVersion + 1, json.GetProperty("consent_version").GetInt32());
        // a stale client sending the old number must not roll it back; skipping ahead is refused too
        await ShouldBe(HttpStatusCode.BadRequest, await c.PutAsync("/api/settings", Body(before)));
        await ShouldBe(HttpStatusCode.BadRequest, await c.PutAsync("/api/settings", Body(before, d => d["consent_version"] = before.ConsentVersion + 5)));
        Assert.Equal(before.ConsentVersion + 1, (await Current()).ConsentVersion);
    }

    [Fact]
    public async Task Only_an_admin_can_change_settings()
    {
        using var c = await AdminAsync();
        var s = await Current();
        var staff = await TestHelpers.NewStaffLoginAsync(F, UserRole.Staff);
        using var sc = F.ClientFor(staff.UserId, "Staff");
        await ShouldBeDenied(await sc.PutAsync("/api/settings", Body(s)));
        using var anon = F.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsync("/api/settings", Body(s))).StatusCode);
    }
}
// JJQR - END
