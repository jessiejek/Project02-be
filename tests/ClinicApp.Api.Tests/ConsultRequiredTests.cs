//JJQR - AI (Nov 24, 2026) - Admin > Settings > Consultation: which parts are required to complete a visit (clinic_settings.consult_required)
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class ConsultRequiredTests(ApiFixture api) : ApiTestBase(api)
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

    private Task<string?> Stored() => WithDb(db => db.ClinicSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => s.ConsultRequired).SingleAsync());

    private Task Reset() => WithDb(async db => { (await db.ClinicSettings.SingleAsync(s => s.Id == 1)).ConsultRequired = null; await db.SaveChangesAsync(); return 0; });

    [Fact]
    public async Task Admin_saves_the_parts_in_a_fixed_order_and_the_settings_show_them()
    {
        using var admin = await AdminAsync();
        try
        {
            await ShouldBe(HttpStatusCode.OK, await admin.PutAsync("/api/settings/consult-required", ApiFactory.Body(new { sections = new[] { "Vitals", "notes", "vitals" } })));
            Assert.Equal("notes,vitals", await Stored());
            Assert.Equal("notes,vitals", (await JsonOf(await admin.GetAsync("/api/settings"))).GetProperty("consult_required").GetString());

            await ShouldBe(HttpStatusCode.OK, await admin.PutAsync("/api/settings/consult-required", ApiFactory.Body(new { sections = Array.Empty<string>() })));
            Assert.Equal("", await Stored()); // nothing but the fee
        }
        finally { await Reset(); }
    }

    [Fact]
    public async Task An_unknown_part_is_refused_and_nothing_changes()
    {
        using var admin = await AdminAsync();
        await Reset();
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PutAsync("/api/settings/consult-required", ApiFactory.Body(new { sections = new[] { "notes", "fee" } })));
        Assert.Null(await Stored());
    }

    [Fact]
    public async Task Only_the_admin_can_change_it()
    {
        await AdminAsync();
        await Reset();
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.Forbidden, await doc.PutAsync("/api/settings/consult-required", ApiFactory.Body(new { sections = new[] { "vitals" } })));
        Assert.Null(await Stored());
    }
}
//JJQR - END
