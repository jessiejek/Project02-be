// JJQR - AI (Nov 20, 2026) - PV-5: "who looked at this patient"
using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class AccessLogTests(ApiFixture api) : ApiTestBase(api)
{
    private Task<int> Rows(Guid patient) => WithDb(db => db.AuditLogs.CountAsync(a => a.EntityType == AuditEntityType.Access && a.EntityId == patient));

    [Fact]
    public async Task Staff_and_doctor_views_are_logged_once_per_window_and_a_patient_viewing_themself_is_not()
    {
        var p = await TestHelpers.NewPatientAsync(F);
        using var staff = F.As(W.Staff, "Staff");
        using var doc = F.As(W.DoctorA, "Doctor");
        using var self = F.ClientFor(p.UserId, "Patient");

        await ShouldBe(HttpStatusCode.OK, await self.GetAsync($"/api/patients/{p.PatientId}"));
        Assert.Equal(0, await Rows(p.PatientId));

        await ShouldBe(HttpStatusCode.OK, await staff.GetAsync($"/api/patients/{p.PatientId}"));
        await ShouldBe(HttpStatusCode.OK, await staff.GetAsync($"/api/patients/{p.PatientId}")); // same person, same area → one row
        Assert.Equal(1, await Rows(p.PatientId));
        await ShouldBe(HttpStatusCode.OK, await doc.GetAsync($"/api/patients/{p.PatientId}")); // different person → new row
        await ShouldBe(HttpStatusCode.OK, await staff.GetAsync($"/api/patients/{p.PatientId}/allergies")); // different area → new row
        Assert.Equal(3, await Rows(p.PatientId));
    }

    [Fact]
    public async Task Only_an_admin_can_read_the_access_list_and_it_names_who_and_what_without_patient_details()
    {
        var p = await TestHelpers.NewPatientAsync(F, "Zelda");
        using var staff = F.As(W.Staff, "Staff");
        await staff.GetAsync($"/api/patients/{p.PatientId}");
        using var admin = F.As(W.Admin, "Admin");
        var res = await admin.GetAsync($"/api/audit-logs/patient/{p.PatientId}/access");
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Patient record", body);
        Assert.DoesNotContain("Zelda", body);
        await ShouldBe(HttpStatusCode.Forbidden, await staff.GetAsync($"/api/audit-logs/patient/{p.PatientId}/access"));
        using var patient = F.ClientFor(p.UserId, "Patient");
        await ShouldBe(HttpStatusCode.Forbidden, await patient.GetAsync($"/api/audit-logs/patient/{p.PatientId}/access"));
    }

    [Fact]
    public async Task The_change_log_does_not_include_access_or_sign_in_rows_unless_asked()
    {
        var p = await TestHelpers.NewPatientAsync(F);
        using var staff = F.As(W.Staff, "Staff");
        await staff.GetAsync($"/api/patients/{p.PatientId}");
        using var admin = F.As(W.Admin, "Admin");
        var def = await admin.GetAsync($"/api/audit-logs/search?pageSize=200");
        Assert.DoesNotContain("\"entity_type\":\"Access\"", await def.Content.ReadAsStringAsync());
        var asked = await admin.GetAsync($"/api/audit-logs/search?entityType=Access&pageSize=200");
        Assert.Contains("\"entity_type\":\"Access\"", await asked.Content.ReadAsStringAsync());
    }
}
// JJQR - END
