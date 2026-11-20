// JJQR - AI (Nov 20, 2026) - PV-3 / PV-5: password rule, lockout after repeated failures, sign-ins in the access log
using System.Net;
using ClinicApp.Auth;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class PasswordAndLockoutTests(ApiFixture api) : ApiTestBase(api)
{
    private static StringContent Login(string email, string pw) => ApiFactory.Body(new { email, password = pw });
    /// <summary>/api/auth/* bodies are camelCase on the wire (the data resources are snake_case).</summary>
    private static StringContent Camel(object o) => new(System.Text.Json.JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    [Theory]
    [InlineData("short1", false)]
    [InlineData("12345678901", false)]          // 11
    [InlineData("password123", false)]          // 11 and common
    [InlineData("password1234", false)]         // 12 but on the common list
    [InlineData("aaaaaaaaaaaa", false)]
    [InlineData("qwertyuiop12", false)]
    [InlineData("Correct-Horse-Battery-9", true)]
    [InlineData("blue pelican window 7", true)]
    public void Policy_accepts_long_uncommon_passwords_only(string pw, bool ok) =>
        Assert.Equal(ok, PasswordPolicy.Check(pw) is null);

    [Fact]
    public void Policy_rejects_a_password_that_contains_the_email_name() =>
        Assert.NotNull(PasswordPolicy.Check("maria.santos-2026!", "maria.santos@clinic.test"));

    [Fact]
    public void Policy_never_echoes_the_password()
    {
        var msg = PasswordPolicy.Check("hunter2-secret")!;
        Assert.DoesNotContain("hunter2", msg);
    }

    [Fact]
    public async Task Change_password_refuses_a_weak_one_and_accepts_a_strong_one()
    {
        var (userId, _, email) = await TestHelpers.NewStaffLoginAsync(F);
        using var c = F.ClientFor(userId, "Staff");
        var weak = await c.PostAsync("/api/auth/change-password", Camel(new { currentPassword = TestHelpers.TestPassword, newPassword = "short-pw" }));
        await ShouldBe(HttpStatusCode.BadRequest, weak);
        var weakBody = await weak.Content.ReadAsStringAsync(); Assert.True(weakBody.Contains("12"), weakBody);
        await ShouldBe(HttpStatusCode.BadRequest, await c.PostAsync("/api/auth/change-password", Camel(new { currentPassword = TestHelpers.TestPassword, newPassword = "password1234" })));
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/auth/change-password", Camel(new { currentPassword = TestHelpers.TestPassword, newPassword = "A-much-longer-passphrase-1" })));
        using var anon = F.Anonymous();
        await ShouldBe(HttpStatusCode.OK, await anon.PostAsync("/api/auth/login", Login(email, "A-much-longer-passphrase-1")));
    }

    [Fact]
    public async Task Register_refuses_a_weak_password()
    {
        using var c = F.Anonymous();
        var res = await c.PostAsync("/api/auth/register", Camel(new
        {
            firstName = "Wea", lastName = "Kpass", email = $"weak-{Guid.NewGuid():N}@test.local", password = "short1",
            dateOfBirth = "1990-01-01", sex = "Female"
        }));
        await ShouldBe(HttpStatusCode.BadRequest, res);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password_until_it_expires()
    {
        var (userId, _, email) = await TestHelpers.NewStaffLoginAsync(F);
        using var c = F.Anonymous();
        for (var i = 0; i < PasswordPolicy.MaxFailedLogins; i++)
            await ShouldBe(HttpStatusCode.Unauthorized, await c.PostAsync("/api/auth/login", Login(email, "wrong-password-" + i)));
        var locked = await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword));
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        Assert.Contains("Try again", await locked.Content.ReadAsStringAsync());

        // time passes → the right password works again and the counter is clear
        await WithDb(async db => { (await db.Users.SingleAsync(u => u.Id == userId)).LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); return 0; });
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword)));
        Assert.Equal(0, await WithDb(db => db.Users.Where(u => u.Id == userId).Select(u => u.FailedLoginCount).SingleAsync()));
    }

    [Fact]
    public async Task A_good_login_between_failures_resets_the_count()
    {
        var (userId, _, email) = await TestHelpers.NewStaffLoginAsync(F);
        using var c = F.Anonymous();
        for (var i = 0; i < PasswordPolicy.MaxFailedLogins - 1; i++)
            await ShouldBe(HttpStatusCode.Unauthorized, await c.PostAsync("/api/auth/login", Login(email, "nope-nope-" + i)));
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword)));
        for (var i = 0; i < PasswordPolicy.MaxFailedLogins - 1; i++)
            await ShouldBe(HttpStatusCode.Unauthorized, await c.PostAsync("/api/auth/login", Login(email, "nope-again-" + i)));
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword)));
    }

    [Fact]
    public async Task Logins_and_failed_logins_are_written_to_the_access_log_without_the_email_or_password()
    {
        var (userId, _, email) = await TestHelpers.NewStaffLoginAsync(F);
        using var c = F.Anonymous();
        await c.PostAsync("/api/auth/login", Login(email, "definitely-wrong-1"));
        await c.PostAsync("/api/auth/login", Login(email, TestHelpers.TestPassword));
        await c.PostAsync("/api/auth/login", Login($"nobody-{Guid.NewGuid():N}@test.local", "whatever-whatever"));
        var rows = await WithDb(db => db.AuditLogs.Where(a => a.EntityType == AuditEntityType.Auth).ToListAsync());
        Assert.Contains(rows, r => r.EntityId == userId && r.Action == "LoginFailed");
        Assert.Contains(rows, r => r.EntityId == userId && r.Action == "Login");
        Assert.Contains(rows, r => r.EntityId == Guid.Empty && r.Action == "LoginFailed");
        Assert.DoesNotContain(rows, r => (r.Details ?? "").Contains("@") || (r.Details ?? "").Contains("definitely-wrong"));
    }
}
// JJQR - END
