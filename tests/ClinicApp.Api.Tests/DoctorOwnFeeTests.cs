using System.Net;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 22, 2026) - owner: a doctor may change their OWN consultation fee (PF); nobody else's, and nobody else may use the endpoint.
[Collection("api")]
public class DoctorOwnFeeTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<decimal> FeeOf(Guid doctorId) => await WithDb(db => db.Doctors.AsNoTracking().Where(d => d.DoctorId == doctorId).Select(d => d.ConsultationFee).SingleAsync());

    [Fact]
    public async Task Doctor_changes_own_fee_and_it_is_audited()
    {
        var before = await FeeOf(W.DoctorA.RowId);
        var newFee = before + 125m;
        var res = await F.As(W.DoctorA, "Doctor").PutAsync("/api/doctors/me", ApiFactory.Body(new { consultation_fee = newFee }));
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.Equal(newFee, await FeeOf(W.DoctorA.RowId));
        var audited = await WithDb(db => db.AuditLogs.AsNoTracking().AnyAsync(a => a.EntityId == W.DoctorA.RowId && a.Action == "OwnFeeChanged"));
        Assert.True(audited);
    }

    [Fact]
    public async Task Changing_my_fee_never_touches_another_doctor()
    {
        var otherBefore = await FeeOf(W.DoctorB.RowId);
        await ShouldBe(HttpStatusCode.OK, await F.As(W.DoctorA, "Doctor").PutAsync("/api/doctors/me", ApiFactory.Body(new { consultation_fee = 777m })));
        Assert.Equal(otherBefore, await FeeOf(W.DoctorB.RowId));
    }

    [Fact]
    public async Task A_negative_fee_is_refused_and_nothing_changes()
    {
        var before = await FeeOf(W.DoctorA.RowId);
        var res = await F.As(W.DoctorA, "Doctor").PutAsync("/api/doctors/me", ApiFactory.Body(new { consultation_fee = -5m }));
        await ShouldBe(HttpStatusCode.BadRequest, res);
        Assert.Equal(before, await FeeOf(W.DoctorA.RowId));
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Patient")]
    [InlineData("Admin")]
    public async Task Only_a_doctor_can_use_the_own_fee_endpoint(string role)
    {
        var who = role switch { "Staff" => W.Staff, "Admin" => W.Admin, _ => W.PatientA };
        var res = await F.As(who, role).PutAsync("/api/doctors/me", ApiFactory.Body(new { consultation_fee = 1m }));
        await ShouldBe(HttpStatusCode.Forbidden, res);
    }

    [Fact]
    public async Task Leaving_the_fee_out_keeps_the_fee()
    {
        var before = await FeeOf(W.DoctorB.RowId);
        await ShouldBe(HttpStatusCode.OK, await F.As(W.DoctorB, "Doctor").PutAsync("/api/doctors/me", ApiFactory.Body(new { bio = "hello" })));
        Assert.Equal(before, await FeeOf(W.DoctorB.RowId));
    }
}
//JJQR - END

//JJQR - AI (Nov 22, 2026) - owner: the doctor also edits their own specialization and full name, and anyone can change their own email.
[Collection("api")]
public class DoctorOwnProfileAndEmailTests(ApiFixture api) : ApiTestBase(api)
{
    private static StringContent Camel(object o) => new(System.Text.Json.JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Doctor_changes_own_specialization_and_full_name()
    {
        var res = await F.As(W.DoctorA, "Doctor").PutAsync("/api/doctors/me", ApiFactory.Body(new { specialization = "Family Medicine (Adult and Pediatrics)", full_name = "Dr. Test Doctor" }));
        await ShouldBe(HttpStatusCode.OK, res);
        var (spec, name) = await WithDb(async db => (await db.Doctors.AsNoTracking().Where(d => d.DoctorId == W.DoctorA.RowId).Select(d => d.Specialization).SingleAsync(),
            await db.StaffAccounts.AsNoTracking().Where(s => s.StaffId == W.DoctorA.RowId).Select(s => s.FullName).SingleAsync()));
        Assert.Equal("Family Medicine (Adult and Pediatrics)", spec);
        Assert.Equal("Dr. Test Doctor", name);
    }

    [Fact]
    public async Task User_changes_own_email_with_the_password_and_it_follows_everywhere()
    {
        var (userId, _, email) = await TestHelpers.NewStaffLoginAsync(F);
        using var c = F.ClientFor(userId, "Staff");
        var newEmail = $"new-{Guid.NewGuid():N}@clinic.test";
        await ShouldBe(HttpStatusCode.BadRequest, await c.PostAsync("/api/auth/change-email", Camel(new { currentPassword = "wrong-password-123", newEmail })));
        await ShouldBe(HttpStatusCode.BadRequest, await c.PostAsync("/api/auth/change-email", Camel(new { currentPassword = TestHelpers.TestPassword, newEmail = "not-an-email" })));
        await ShouldBe(HttpStatusCode.OK, await c.PostAsync("/api/auth/change-email", Camel(new { currentPassword = TestHelpers.TestPassword, newEmail })));
        using var anon = F.Anonymous();
        var login = await anon.PostAsync("/api/auth/login", Camel(new { email = newEmail, password = TestHelpers.TestPassword }));
        await ShouldBe(HttpStatusCode.OK, login);
        await ShouldBe(HttpStatusCode.Unauthorized, await anon.PostAsync("/api/auth/login", Camel(new { email, password = TestHelpers.TestPassword })));
    }

    [Fact]
    public async Task Two_people_cannot_share_an_email()
    {
        var (u1, _, e1) = await TestHelpers.NewStaffLoginAsync(F);
        var (u2, _, _) = await TestHelpers.NewStaffLoginAsync(F);
        using var c2 = F.ClientFor(u2, "Staff");
        await ShouldBe(HttpStatusCode.Conflict, await c2.PostAsync("/api/auth/change-email", Camel(new { currentPassword = TestHelpers.TestPassword, newEmail = e1 })));
    }

    [Fact]
    public async Task Admin_changes_a_staff_email_and_others_cannot()
    {
        var newEmail = $"by-admin-{Guid.NewGuid():N}@clinic.test";
        await ShouldBe(HttpStatusCode.Forbidden, await F.As(W.Staff, "Staff").PutAsync($"/api/staff-accounts/{W.DoctorA.RowId}/email", ApiFactory.Body(new { email = newEmail })));
        await ShouldBe(HttpStatusCode.OK, await F.As(W.Admin, "Admin").PutAsync($"/api/staff-accounts/{W.DoctorA.RowId}/email", ApiFactory.Body(new { email = newEmail })));
        var stored = await WithDb(db => db.StaffAccounts.AsNoTracking().Where(s => s.StaffId == W.DoctorA.RowId).Select(s => s.Email).SingleAsync());
        Assert.Equal(newEmail, stored);
    }
}
//JJQR - END
