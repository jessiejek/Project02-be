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
