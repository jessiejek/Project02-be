// JJQR - AI (Nov 20, 2026) - B-32: deactivated accounts cannot log in, refresh, or reuse tokens
using System.Net;
using System.Text.Json;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class AccountSecurityTests(ApiFixture api) : ApiTestBase(api)
{
    private static StringContent Login(string email, string pw) => ApiFactory.Body(new { email, password = pw });

    [Fact]
    public async Task Active_staff_can_log_in()
    {
        var (_, _, email) = await TestHelpers.NewStaffLoginAsync(F);
        using var c = F.Anonymous();
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword)));
    }

    [Fact]
    public async Task Deactivated_staff_cannot_log_in()
    {
        var (_, _, email) = await TestHelpers.NewStaffLoginAsync(F, status: StaffStatus.Inactive);
        using var c = F.Anonymous();
        var res = await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword));
        await ShouldBe(HttpStatusCode.Forbidden, res);
        Assert.Contains("deactivated", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deactivating_an_account_kills_its_existing_access_and_refresh_tokens()
    {
        var (userId, staffId, email) = await TestHelpers.NewStaffLoginAsync(F, UserRole.Staff);
        using var anon = F.Anonymous();
        var login = await JsonOf(await anon.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword)));
        var access = login.GetProperty("accessToken").GetString()!;
        var refresh = login.GetProperty("refreshToken").GetString()!;

        using var user = F.CreateClient();
        user.DefaultRequestHeaders.Authorization = new("Bearer", access);
        await ShouldBe(HttpStatusCode.OK, await user.GetAsync("/api/auth/me"));

        using var admin = F.As(W.Admin, "Admin");
        var existing = await JsonOf(await admin.GetAsync($"/api/staff-accounts/{staffId}"));
        var payload = existing.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        payload["status"] = "Inactive";
        await ShouldBe(HttpStatusCode.OK, await admin.PutAsync($"/api/staff-accounts/{staffId}", ApiFactory.Body(payload)));

        // the still-unexpired access token is rejected straight away
        await ShouldBe(HttpStatusCode.Unauthorized, await user.GetAsync("/api/auth/me"));
        // the refresh token no longer mints a new session
        var refreshRes = await anon.PostAsync("/api/auth/refresh-token", new StringContent(JsonSerializer.Serialize(new { refreshToken = refresh }), System.Text.Encoding.UTF8, "application/json"));
        await ShouldBe(HttpStatusCode.Unauthorized, refreshRes);

        // reactivating restores login
        payload["status"] = "Active";
        await ShouldBe(HttpStatusCode.OK, await admin.PutAsync($"/api/staff-accounts/{staffId}", ApiFactory.Body(payload)));
        await ShouldBe(HttpStatusCode.OK, await anon.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword)));
    }

    [Fact]
    public async Task A_token_for_a_deleted_user_is_rejected()
    {
        var ghost = F.ClientFor(Guid.NewGuid(), "Staff");
        await ShouldBe(HttpStatusCode.Unauthorized, await ghost.GetAsync("/api/auth/me"));
    }
}
// JJQR - END
