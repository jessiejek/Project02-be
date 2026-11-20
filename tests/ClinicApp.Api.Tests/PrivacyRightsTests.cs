// JJQR - AI (Nov 20, 2026) - PV-12/13/14: consent decisions, data download, correction/deletion requests
using System.Net;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class PrivacyRightsTests(ApiFixture api) : ApiTestBase(api)
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public async Task Consent_decisions_are_the_patients_own_and_each_one_is_kept_as_a_dated_row()
    {
        var p = await TestHelpers.NewPatientAsync(F);
        using var me = F.ClientFor(p.UserId, "Patient");
        using var staff = F.As(W.Staff, "Staff");

        await ShouldBeDenied(await staff.PostAsync($"/api/patients/{p.PatientId}/consent-decision", ApiFactory.Body(new { decision = "Accepted" })));
        await ShouldBe(HttpStatusCode.BadRequest, await me.PostAsync($"/api/patients/{p.PatientId}/consent-decision", ApiFactory.Body(new { decision = "Maybe" })));
        await ShouldBe(HttpStatusCode.BadRequest, await me.PostAsync($"/api/patients/{p.PatientId}/consent-decision", ApiFactory.Body(new { decision = "Withdrawn" }))); // nothing to withdraw yet

        await ShouldBe(HttpStatusCode.OK, await me.PostAsync($"/api/patients/{p.PatientId}/consent-decision", ApiFactory.Body(new { decision = "Declined" })));
        var afterDecline = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == p.PatientId));
        Assert.Null(afterDecline.ConsentedAt);

        await ShouldBe(HttpStatusCode.OK, await me.PostAsync($"/api/patients/{p.PatientId}/consent-decision", ApiFactory.Body(new { decision = "Accepted" })));
        Assert.NotNull((await WithDb(db => db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == p.PatientId))).ConsentedAt);

        await ShouldBe(HttpStatusCode.OK, await me.PostAsync($"/api/patients/{p.PatientId}/consent-decision", ApiFactory.Body(new { decision = "Withdrawn" })));
        var after = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == p.PatientId));
        Assert.Null(after.ConsentedAt);
        Assert.NotNull(after.ConsentWithdrawnAt);

        var rows = await WithDb(db => db.PatientConsents.Where(c => c.PatientId == p.PatientId).OrderBy(c => c.DecidedAt).Select(c => c.Decision).ToListAsync());
        Assert.Equal(new[] { "Declined", "Accepted", "Withdrawn" }, rows);

        var hist = await me.GetAsync($"/api/patients/{p.PatientId}/consents");
        await ShouldBe(HttpStatusCode.OK, hist);
        Assert.Equal(3, Json(await hist.Content.ReadAsStringAsync()).GetArrayLength());
        await ShouldBeDenied(await staff.GetAsync($"/api/patients/{p.PatientId}/consents"));
    }

    [Fact]
    public async Task Old_consent_PUT_is_patient_only_and_refuses_a_version_that_was_never_published()
    {
        var p = await TestHelpers.NewPatientAsync(F);
        using var me = F.ClientFor(p.UserId, "Patient");
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBeDenied(await staff.PutAsync($"/api/patients/{p.PatientId}/consent", ApiFactory.Body(1)));
        await ShouldBe(HttpStatusCode.BadRequest, await me.PutAsync($"/api/patients/{p.PatientId}/consent", ApiFactory.Body(9999)));
        await ShouldBe(HttpStatusCode.OK, await me.PutAsync($"/api/patients/{p.PatientId}/consent", ApiFactory.Body(1)));
        Assert.Equal(1, await WithDb(db => db.PatientConsents.CountAsync(c => c.PatientId == p.PatientId && c.Decision == "Accepted")));
    }

    [Fact]
    public async Task Download_my_data_is_own_data_only_and_never_exposes_file_urls()
    {
        var p = await TestHelpers.NewPatientAsync(F, "Downloadia");
        var other = await TestHelpers.NewPatientAsync(F);
        using var me = F.ClientFor(p.UserId, "Patient");
        using var staff = F.As(W.Staff, "Staff");
        using var admin = F.As(W.Admin, "Admin");

        var res = await me.GetAsync($"/api/patients/{p.PatientId}/export");
        await ShouldBe(HttpStatusCode.OK, res);
        var text = await res.Content.ReadAsStringAsync();
        Assert.Contains("Downloadia", text);
        Assert.DoesNotContain("file_url", text);
        Assert.Contains("attachment", res.Content.Headers.ContentDisposition?.ToString() ?? string.Join(",", res.Headers.Select(h => h.Key + string.Join("", h.Value))));
        await ShouldBeDenied(await me.GetAsync($"/api/patients/{other.PatientId}/export"));
        await ShouldBeDenied(await staff.GetAsync($"/api/patients/{p.PatientId}/export"));
        await ShouldBeDenied(await admin.GetAsync($"/api/patients/{p.PatientId}/export"));
    }

    [Fact]
    public async Task Correction_and_deletion_requests_are_logged_and_a_deletion_waits_for_the_7_year_retention()
    {
        using var a = F.Patient(W.PatientA); // has a booking on file
        var res = await a.PostAsync("/api/privacy/data-requests", ApiFactory.Body(new { kind = "Deletion", details = "Please delete my records." }));
        await ShouldBe(HttpStatusCode.OK, res);
        var j = Json(await res.Content.ReadAsStringAsync());
        Assert.Equal("Scheduled", j.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, j.GetProperty("retain_until").ValueKind);
        var id = j.GetProperty("id").GetGuid();

        await ShouldBe(HttpStatusCode.Conflict, await a.PostAsync("/api/privacy/data-requests", ApiFactory.Body(new { kind = "Deletion", details = "again" })));
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/privacy/data-requests", ApiFactory.Body(new { kind = "Nope", details = "x" })));
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/privacy/data-requests", ApiFactory.Body(new { kind = "Correction", details = "" })));

        // the audit row says what kind and when, not what the patient wrote
        var audit = await WithDb(db => db.AuditLogs.Where(x => x.EntityId == id).Select(x => x.Details).ToListAsync());
        Assert.Single(audit);
        Assert.DoesNotContain("delete my records", audit[0]!);

        using var staff = F.As(W.Staff, "Staff");
        await ShouldBeDenied(await staff.GetAsync("/api/privacy/data-requests"));
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.GetAsync("/api/privacy/data-requests"));
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PutAsync($"/api/privacy/data-requests/{id}", ApiFactory.Body(new { status = "Completed" })));
        await ShouldBe(HttpStatusCode.OK, await admin.PutAsync($"/api/privacy/data-requests/{id}", ApiFactory.Body(new { status = "Rejected", note = "Retention applies." })));

        var mine = await a.GetAsync("/api/privacy/data-requests/mine");
        await ShouldBe(HttpStatusCode.OK, mine);
        Assert.Contains("Retention applies.", await mine.Content.ReadAsStringAsync());
        await WithDb(async db => { db.DataRequests.RemoveRange(db.DataRequests.Where(r => r.Id == id)); await db.SaveChangesAsync(); return 0; });
    }

    [Fact]
    public async Task A_correction_request_from_a_patient_without_visits_is_Pending_and_staff_cannot_file_requests()
    {
        var p = await TestHelpers.NewPatientAsync(F);
        using var me = F.ClientFor(p.UserId, "Patient");
        var res = await me.PostAsync("/api/privacy/data-requests", ApiFactory.Body(new { kind = "Correction", details = "My birth date is wrong." }));
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.Equal("Pending", Json(await res.Content.ReadAsStringAsync()).GetProperty("status").GetString());
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBeDenied(await staff.PostAsync("/api/privacy/data-requests", ApiFactory.Body(new { kind = "Correction", details = "x" })));
    }

    // JJQR - AI (Nov 20, 2026) - PV-2: patients don't get a doctor's personal email / phone from the staff-accounts endpoint
    [Fact]
    public async Task A_patient_sees_a_doctors_name_but_not_the_doctors_email_or_phone()
    {
        using var a = F.Patient(W.PatientA);
        var asPatient = Json(await (await a.GetAsync($"/api/staff-accounts/{W.DoctorA.RowId}")).Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(asPatient.GetProperty("full_name").GetString()));
        Assert.Equal("", asPatient.GetProperty("email").GetString());
        Assert.NotEqual(JsonValueKind.String, asPatient.GetProperty("contact_number").ValueKind);
        using var staff = F.As(W.Staff, "Staff");
        var asStaff = Json(await (await staff.GetAsync($"/api/staff-accounts/{W.DoctorA.RowId}")).Content.ReadAsStringAsync());
        Assert.NotEqual("", asStaff.GetProperty("email").GetString());
    }
    // JJQR - END
}
// JJQR - END
