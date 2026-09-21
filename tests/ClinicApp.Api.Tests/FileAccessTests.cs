using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

/// <summary>P0.6 — patient documents and lab results are PHI. There must be no unauthenticated or
/// guessable URL that returns them; the only read path is the authorized `/file` endpoint.</summary>
[Collection("api")]
public class FileAccessTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<string> StoredUrl(Guid documentId) =>
        await WithDb(db => db.PatientDocuments.Where(d => d.Id == documentId).Select(d => d.FileUrl).SingleAsync());

    // ── the old public /uploads mount is gone ───────────────────────────
    [Fact]
    public async Task Stored_upload_path_is_not_served_to_anyone()
    {
        var url = await StoredUrl(W.DocumentB);
        using var anon = F.Anonymous();
        await ShouldBe(HttpStatusCode.NotFound, await anon.GetAsync(url));
        using var owner = F.Patient(W.PatientB);
        await ShouldBe(HttpStatusCode.NotFound, await owner.GetAsync(url)); // not even with a valid token: no static mount at all
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.NotFound, await admin.GetAsync(url));
    }

    [Theory]
    [InlineData("patient-documents")]
    [InlineData("patient-lab-results")]
    public async Task File_endpoints_require_authentication(string kind)
    {
        var id = kind == "patient-documents" ? W.DocumentA : W.LabResultA;
        using var anon = F.Anonymous();
        await ShouldBe(HttpStatusCode.Unauthorized, await anon.GetAsync($"/api/{kind}/{id}/file"));
    }

    // ── owner / staff can read; other patients cannot ───────────────────
    [Fact]
    public async Task Owner_can_download_their_document_and_lab_result()
    {
        using var a = F.Patient(W.PatientA);
        var doc = await a.GetAsync($"/api/patient-documents/{W.DocumentA}/file");
        await ShouldBe(HttpStatusCode.OK, doc);
        Assert.Equal(World.DocumentAContent, await doc.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", doc.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", doc.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", doc.Headers.CacheControl?.ToString());

        var lab = await a.GetAsync($"/api/patient-lab-results/{W.LabResultA}/file");
        await ShouldBe(HttpStatusCode.OK, lab);
        Assert.Equal("A-LAB", await lab.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Other_patient_cannot_download_a_document_or_lab_result_by_id()
    {
        using var a = F.Patient(W.PatientA);
        var doc = await a.GetAsync($"/api/patient-documents/{W.DocumentB}/file");
        await ShouldBe(HttpStatusCode.NotFound, doc);
        Assert.DoesNotContain(World.DocumentBContent, await doc.Content.ReadAsStringAsync());
        await ShouldBe(HttpStatusCode.NotFound, await a.GetAsync($"/api/patient-lab-results/{W.LabResultB}/file"));

        // Indistinguishable from a nonexistent id.
        await ShouldBe(HttpStatusCode.NotFound, await a.GetAsync($"/api/patient-documents/{Guid.NewGuid()}/file"));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Staff")]
    [InlineData("Doctor")]
    public async Task Clinic_staff_can_download_any_patients_file(string role)
    {
        using var c = F.ClientFor(role switch { "Admin" => W.Admin.UserId, "Staff" => W.Staff.UserId, _ => W.DoctorA.UserId }, role);
        var res = await c.GetAsync($"/api/patient-documents/{W.DocumentB}/file");
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.Equal(World.DocumentBContent, await res.Content.ReadAsStringAsync());
    }

    // ── listing ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Document_and_lab_lists_only_return_the_callers_own_rows()
    {
        using var a = F.Patient(W.PatientA);
        var docs = await ListOf(await a.GetAsync("/api/patient-documents"));
        Assert.True(AnyValue(docs, "id", W.DocumentA));
        Assert.False(AnyValue(docs, "id", W.DocumentB));
        var labs = await ListOf(await a.GetAsync("/api/patient-lab-results"));
        Assert.True(AnyValue(labs, "id", W.LabResultA));
        Assert.False(AnyValue(labs, "id", W.LabResultB));

        await ShouldBeDenied(await a.GetAsync($"/api/patient-documents?patientId={W.PatientB.RowId}"));
        await ShouldBeDenied(await a.GetAsync($"/api/patient-lab-results?patientId={W.PatientB.RowId}"));
        Assert.Empty(await ListOf(await a.GetAsync($"/api/patient-documents?bookingId={W.BookingB}")));
        Assert.Empty(await ListOf(await a.GetAsync($"/api/patient-lab-results?bookingId={W.BookingB}")));
    }

    // ── upload ──────────────────────────────────────────────────────────
    private static MultipartFormDataContent Upload(Guid patientId, Guid bookingId, string contentType = "application/pdf",
        string content = "uploaded", Guid? consultationId = null, Guid? labOrderId = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(patientId.ToString()), "patientId" },
            { new StringContent(bookingId.ToString()), "bookingId" }
        };
        if (consultationId is not null) form.Add(new StringContent(consultationId.ToString()!), "consultationId");
        if (labOrderId is not null) form.Add(new StringContent(labOrderId.ToString()!), "labOrderId");
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "scan.pdf");
        return form;
    }

    [Fact]
    public async Task Patient_can_upload_to_own_booking_and_then_download_it()
    {
        using var a = F.Patient(W.PatientA);
        var res = await a.PostAsync("/api/patient-documents", Upload(W.PatientA.RowId, W.BookingA, content: "fresh-upload"));
        await ShouldBe(HttpStatusCode.OK, res);
        var id = (await JsonOf(res)).GetProperty("id").GetGuid();
        var dl = await a.GetAsync($"/api/patient-documents/{id}/file");
        await ShouldBe(HttpStatusCode.OK, dl);
        Assert.Equal("fresh-upload", await dl.Content.ReadAsStringAsync());

        using var b = F.Patient(W.PatientB);
        await ShouldBe(HttpStatusCode.NotFound, await b.GetAsync($"/api/patient-documents/{id}/file"));
    }

    [Fact]
    public async Task Patient_cannot_upload_into_another_patients_record()
    {
        using var a = F.Patient(W.PatientA);
        // Straight impersonation.
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync("/api/patient-documents", Upload(W.PatientB.RowId, W.BookingB)));
        await ShouldBe(HttpStatusCode.Forbidden, await a.PostAsync("/api/patient-lab-results", Upload(W.PatientB.RowId, W.BookingB)));
        // Own patient id, but hanging the file off someone else's booking / consultation / lab order.
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/patient-documents", Upload(W.PatientA.RowId, W.BookingB)));
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/patient-documents", Upload(W.PatientA.RowId, W.BookingA, consultationId: W.ConsultationB)));
        var bobLabOrder = await WithDb(db => db.LabOrders.Where(l => l.PatientId == W.PatientB.RowId).Select(l => l.LabOrderId).FirstAsync());
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/patient-lab-results", Upload(W.PatientA.RowId, W.BookingA, labOrderId: bobLabOrder)));
        Assert.False(await WithDb(db => db.PatientDocuments.AnyAsync(d => d.PatientId == W.PatientB.RowId && d.FileName == "scan.pdf")));
    }

    [Fact]
    public async Task Upload_rejects_disallowed_content_types()
    {
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync("/api/patient-documents", Upload(W.PatientA.RowId, W.BookingA, contentType: "text/html", content: "<script>alert(1)</script>")));
    }

    [Fact]
    public async Task Anonymous_cannot_upload()
    {
        using var anon = F.Anonymous();
        await ShouldBe(HttpStatusCode.Unauthorized, await anon.PostAsync("/api/patient-documents", Upload(W.PatientA.RowId, W.BookingA)));
    }

    // ── hostile stored values ───────────────────────────────────────────
    [Fact]
    public async Task Path_traversal_in_a_stored_url_never_escapes_the_upload_root()
    {
        var secret = Path.Combine(F.Root, "secret.txt"); // sibling of the uploads folder
        await File.WriteAllTextAsync(secret, "TOP-SECRET");
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.PatientDocuments.Add(new PatientDocument { Id = id, PatientId = W.PatientA.RowId, BookingId = W.BookingA, FileName = "x.pdf", FileContentType = "application/pdf", FileUrl = "/uploads/../secret.txt", UploadedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });

        using var a = F.Patient(W.PatientA);
        var res = await a.GetAsync($"/api/patient-documents/{id}/file");
        await ShouldBe(HttpStatusCode.NotFound, res);
        Assert.DoesNotContain("TOP-SECRET", await res.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("https://other.example/x.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task Non_upload_urls_are_never_resolved(string stored)
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.PatientDocuments.Add(new PatientDocument { Id = id, PatientId = W.PatientA.RowId, BookingId = W.BookingA, FileName = "x.pdf", FileContentType = "application/pdf", FileUrl = stored, UploadedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.NotFound, await a.GetAsync($"/api/patient-documents/{id}/file"));
    }

    [Fact]
    public async Task Stored_content_type_outside_the_allow_list_is_served_as_opaque_bytes()
    {
        var real = await StoredUrl(W.DocumentA);
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.PatientDocuments.Add(new PatientDocument { Id = id, PatientId = W.PatientA.RowId, BookingId = W.BookingA, FileName = "x.html", FileContentType = "text/html", FileUrl = real, UploadedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });
        using var a = F.Patient(W.PatientA);
        var res = await a.GetAsync($"/api/patient-documents/{id}/file");
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.Equal("application/octet-stream", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Missing_file_on_disk_is_a_404_not_a_500()
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.PatientDocuments.Add(new PatientDocument { Id = id, PatientId = W.PatientA.RowId, BookingId = W.BookingA, FileName = "gone.pdf", FileContentType = "application/pdf", FileUrl = $"/uploads/{W.PatientA.RowId}/{W.BookingA}/gone.pdf", UploadedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });
        using var a = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.NotFound, await a.GetAsync($"/api/patient-documents/{id}/file"));
    }
}
