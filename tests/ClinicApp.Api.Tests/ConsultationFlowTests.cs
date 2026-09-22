using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

/// <summary>Consultation immutability (§17.3 #15) against the *real* doctor-page save sequence:
/// `persistConsultation(status)` = PUT consultation, then PUT diagnoses (+ labs, follow-up…), then
/// the queue "complete". The rules must protect a finalized record without breaking that sequence.</summary>
[Collection("api")]
public class ConsultationFlowTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");

    private async Task<Guid> NewBooking(BookingStatus status = BookingStatus.InProgress)
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            (await db.Bookings.SingleAsync(b => b.BookingId == booking)).Status = status;
            await db.SaveChangesAsync();
            return 0;
        });
        return booking;
    }

    private StringContent Save(string status, string complaint = "cough", object? extra = null) =>
        ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId,
            ["status"] = status, ["chief_complaint"] = complaint, ["assessment"] = "URTI",
            ["visit_type"] = "New", ["pf_decision"] = "Charge", ["pf_amount"] = 450m
        });

    private static StringContent Dx(params string[] names) =>
        ApiFactory.Body(names.Select(n => new { custom_description = n, type = "Primary" }).ToArray());

    private Task<Consultation> Consult(Guid booking) =>
        WithDb(db => db.Consultations.AsNoTracking().SingleAsync(c => c.BookingId == booking));

    private Task<int> AuditCount(Guid consultationId, string? action = null) =>
        WithDb(db => db.AuditLogs.CountAsync(a => a.EntityId == consultationId && (action == null || a.Action == action)));

    private async Task<Guid> SaveOk(HttpClient doc, Guid booking, string status, string complaint = "cough")
    {
        var res = await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save(status, complaint));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("consultation_id").GetGuid();
    }

    // ── the normal completion sequence ───────────────────────────────────
    [Fact]
    public async Task Completing_a_visit_saves_diagnoses_and_stays_Completed()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        var id = await SaveOk(doc, booking, "Completed");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("URTI", "Cough")));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{booking}/complete", null));

        var c = await Consult(booking);
        Assert.Equal(ConsultationStatus.Completed, c.Status); // NOT silently "Amended"
        Assert.Equal(2, await WithDb(db => db.ConsultationDiagnoses.CountAsync(d => d.ConsultationId == id)));
        Assert.Equal(BookingStatus.Completed, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));
    }

    [Fact]
    public async Task A_draft_with_saved_diagnoses_can_be_edited_and_completed()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        var id = await SaveOk(doc, booking, "Draft");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Draft dx")));

        // The doctor changes the diagnoses and completes: PUT Completed, then PUT new diagnoses.
        await SaveOk(doc, booking, "Completed");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Final dx")));

        Assert.Equal(ConsultationStatus.Completed, (await Consult(booking)).Status);
        Assert.Equal(new[] { "Final dx" }, await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == id).Select(d => d.CustomDescription!).ToListAsync()));
    }

    // ── retries / double-clicks ──────────────────────────────────────────
    [Fact]
    public async Task Re_saving_the_same_Completed_consultation_is_an_idempotent_no_op()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        var id = await SaveOk(doc, booking, "Completed");
        var before = await Consult(booking);
        var audits = await AuditCount(id);

        await SaveOk(doc, booking, "Completed"); // double-click / retry
        await SaveOk(doc, booking, "Completed");

        var after = await Consult(booking);
        Assert.Equal(ConsultationStatus.Completed, after.Status);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);      // nothing was rewritten
        Assert.Equal(before.CompletedAt, after.CompletedAt);
        Assert.Equal(audits, await AuditCount(id));           // and no duplicate audit rows
    }

    [Fact]
    public async Task A_retry_after_a_partial_failure_recovers()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        var id = await SaveOk(doc, booking, "Completed");   // first attempt: consultation saved…
        // …then the diagnoses call "fails" (never sent). The doctor clicks Complete again:
        await SaveOk(doc, booking, "Completed");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("URTI")));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{booking}/complete", null));
        Assert.Equal(ConsultationStatus.Completed, (await Consult(booking)).Status);
    }

    // ── immutability still holds ─────────────────────────────────────────
    [Theory]
    [InlineData("chief_complaint", "something else")]
    public async Task Changing_content_of_a_Completed_consultation_is_rejected_until_amended(string field, string value)
    {
        var booking = await NewBooking();
        using var doc = DocA();
        await SaveOk(doc, booking, "Completed");
        var body = new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Completed",
            ["chief_complaint"] = "cough", ["assessment"] = "URTI", ["visit_type"] = "New", ["pf_decision"] = "Charge", ["pf_amount"] = 450m,
            [field] = value
        };
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(body)));
        Assert.Equal("cough", (await Consult(booking)).ChiefComplaint);
    }

    [Fact]
    public async Task Fee_affecting_changes_count_as_changes_too()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        await SaveOk(doc, booking, "Completed");
        var res = await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Completed",
            ["chief_complaint"] = "cough", ["assessment"] = "URTI", ["pf_decision"] = "Charge", ["pf_amount"] = 450m,
            ["visit_type"] = "FollowUp"   // changes the fee
        }));
        await ShouldBe(HttpStatusCode.Conflict, res);
    }

    [Fact]
    public async Task A_finalized_consultation_never_goes_back_to_Draft()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        await SaveOk(doc, booking, "Completed");
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/consultations/by-booking/{booking}", Save("Draft")));
        Assert.Equal(ConsultationStatus.Completed, (await Consult(booking)).Status);
    }

    [Fact]
    public async Task Diagnoses_of_an_old_Completed_consultation_are_sealed_until_it_is_amended()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        var id = await SaveOk(doc, booking, "Completed");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Original")));

        // Ten minutes later — well outside the completion window.
        await WithDb(async db =>
        {
            (await db.Consultations.SingleAsync(c => c.ConsultationId == id)).CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
            return 0;
        });

        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Rewritten history")));
        Assert.Equal(new[] { "Original" }, await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == id).Select(d => d.CustomDescription!).ToListAsync()));
        Assert.Equal(ConsultationStatus.Completed, (await Consult(booking)).Status); // and the request did not flip the status
    }

    [Fact]
    public async Task The_amend_flow_works_and_leaves_an_audit_trail()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        var id = await SaveOk(doc, booking, "Completed");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Original")));
        await WithDb(async db =>
        {
            (await db.Consultations.SingleAsync(c => c.ConsultationId == id)).CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
            return 0;
        });

        // Amend = PUT consultation as Amended, then PUT diagnoses (the doctor page's amend save).
        await SaveOk(doc, booking, "Amended", "cough, revised");
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Corrected")));

        Assert.Equal(ConsultationStatus.Amended, (await Consult(booking)).Status);
        Assert.Equal(new[] { "Corrected" }, await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == id).Select(d => d.CustomDescription!).ToListAsync()));
        Assert.Equal(1, await AuditCount(id, "Diagnoses amended"));
        var details = await WithDb(db => db.AuditLogs.Where(a => a.EntityId == id && a.Action == "Diagnoses amended").Select(a => a.Details!).SingleAsync());
        Assert.Contains("Original", details);
        Assert.Contains("Corrected", details);

        // Re-saving identical diagnoses on an Amended record is not a change → no extra audit row.
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{id}/diagnoses", Dx("Corrected")));
        Assert.Equal(1, await AuditCount(id, "Diagnoses amended"));
    }

    [Fact]
    public async Task An_Amended_consultation_can_be_amended_again()
    {
        var booking = await NewBooking();
        using var doc = DocA();
        await SaveOk(doc, booking, "Completed");
        await SaveOk(doc, booking, "Amended", "v2");
        await SaveOk(doc, booking, "Amended", "v3");
        Assert.Equal("v3", (await Consult(booking)).ChiefComplaint);
    }

    [Fact]
    public async Task Another_doctor_still_cannot_touch_the_diagnoses()
    {
        var booking = await NewBooking();
        using var a = DocA();
        var id = await SaveOk(a, booking, "Completed");
        using var b = F.As(W.DoctorB, "Doctor");
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/consultations/{id}/diagnoses", Dx("hijack")));
    }
}
