using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicApp.Infrastructure.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>patient_documents / patient_lab_results (contract §4, §8). Multipart
/// upload → local disk (App_Data/uploads) → row insert.
///
/// Files are PHI, so they are NOT served as static files: the only way to read the
/// bytes is `GET …/{id}/file`, which requires a bearer token and the caller to be
/// the owning patient or staff-like (docs/AUTHZ_MATRIX.md). `file_url` in the row
/// stays as the internal storage key and is not fetchable on its own.</summary>
[ApiController]
[Authorize]
[Route("api")]
public class PatientFilesController(ClinicAppDbContext db, IFileStorageService storage, ActorResolver actors) : ControllerBase
{
    // ── patient_documents ─────────────────────────────────────────────────
    [HttpGet("patient-documents")]
    public async Task<ActionResult<List<PatientDocument>>> GetDocuments(
        [FromQuery] Guid? patientId, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!TryScope(actor, ref patientId)) return Forbid();

        var q = db.PatientDocuments.AsNoTracking()
            .Include(d => d.Booking).ThenInclude(b => b!.Doctor).ThenInclude(d => d!.StaffAccount).AsQueryable();
        if (patientId is not null) q = q.Where(d => d.PatientId == patientId);
        if (bookingId is not null) q = q.Where(d => d.BookingId == bookingId);
        return Ok(await q.OrderByDescending(d => d.UploadedAt).ToListAsync(ct));
    }

    [HttpGet("patient-documents/{id:guid}/file")]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken ct)
    {
        var doc = await db.PatientDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.CanAccessPatient(doc.PatientId)) return NotFound(); // don't confirm the id exists
        return ServeFile(doc.FileUrl, doc.FileName, doc.FileContentType);
    }

    [HttpPost("patient-documents")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<PatientDocument>> UploadDocument(
        [FromForm] Guid patientId, [FromForm] Guid bookingId, [FromForm] Guid? consultationId,
        [FromForm] string? title, [FromForm] string? description, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file." });

        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.CanAccessPatient(patientId)) return Forbid();
        if (!await BookingBelongsToPatientAsync(bookingId, patientId, ct))
            return BadRequest(new { message = "Booking does not belong to this patient." });
        if (consultationId is not null &&
            !await db.Consultations.AnyAsync(c => c.ConsultationId == consultationId && c.PatientId == patientId, ct))
            return BadRequest(new { message = "Consultation does not belong to this patient." });

        string url;
        try
        {
            await using var s = file.OpenReadStream();
            url = await storage.SaveAsync(patientId, bookingId, file.FileName, s, file.ContentType, ct);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        var row = new PatientDocument
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            BookingId = bookingId,
            ConsultationId = consultationId,
            FileName = file.FileName,
            FileSize = file.Length,
            FileContentType = file.ContentType,
            Title = title,
            Description = description,
            FileUrl = url,
            UploadedByUserId = actor.UserId,
            UploadedAt = DateTimeOffset.UtcNow
        };
        db.PatientDocuments.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    // ── patient_lab_results ──────────────────────────────────────────────
    [HttpGet("patient-lab-results")]
    public async Task<ActionResult<List<PatientLabResult>>> GetLabResults(
        [FromQuery] Guid? patientId, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!TryScope(actor, ref patientId)) return Forbid();

        var q = db.PatientLabResults.AsNoTracking()
            .Include(r => r.Booking).ThenInclude(b => b!.Doctor).ThenInclude(d => d!.StaffAccount).AsQueryable();
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        if (bookingId is not null) q = q.Where(r => r.BookingId == bookingId);
        return Ok(await q.OrderByDescending(r => r.UploadedAt).ToListAsync(ct));
    }

    [HttpGet("patient-lab-results/{id:guid}/file")]
    public async Task<IActionResult> DownloadLabResult(Guid id, CancellationToken ct)
    {
        var row = await db.PatientLabResults.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.CanAccessPatient(row.PatientId)) return NotFound();
        return ServeFile(row.FileUrl, row.FileName, row.FileContentType);
    }

    [HttpPost("patient-lab-results")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<PatientLabResult>> UploadLabResult(
        [FromForm] Guid patientId, [FromForm] Guid bookingId, [FromForm] Guid? consultationId,
        [FromForm] Guid? labOrderId, [FromForm] string? resultTitle, [FromForm] string? resultText,
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file." });

        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.CanAccessPatient(patientId)) return Forbid();
        if (!await BookingBelongsToPatientAsync(bookingId, patientId, ct))
            return BadRequest(new { message = "Booking does not belong to this patient." });
        if (consultationId is not null &&
            !await db.Consultations.AnyAsync(c => c.ConsultationId == consultationId && c.PatientId == patientId, ct))
            return BadRequest(new { message = "Consultation does not belong to this patient." });
        if (labOrderId is not null &&
            !await db.LabOrders.AnyAsync(l => l.LabOrderId == labOrderId && l.PatientId == patientId, ct))
            return BadRequest(new { message = "Lab order does not belong to this patient." });

        string url;
        try
        {
            await using var s = file.OpenReadStream();
            url = await storage.SaveAsync(patientId, bookingId, file.FileName, s, file.ContentType, ct);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        var row = new PatientLabResult
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            BookingId = bookingId,
            ConsultationId = consultationId,
            LabOrderId = labOrderId,
            FileName = file.FileName,
            FileContentType = file.ContentType,
            ResultTitle = resultTitle,
            ResultText = resultText,
            Status = "Completed",
            FileUrl = url,
            UploadedAt = DateTimeOffset.UtcNow
        };
        db.PatientLabResults.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    /// <summary>List scoping: a patient only ever sees their own rows (an explicit
    /// filter for someone else's patient id is refused); staff-like roles see any.</summary>
    private static bool TryScope(Actor actor, ref Guid? patientId)
    {
        if (actor.IsStaffLike) return true;
        if (!actor.IsPatient || actor.PatientId is null) return false;
        if (patientId is not null && patientId != actor.PatientId) return false;
        patientId = actor.PatientId;
        return true;
    }

    private Task<bool> BookingBelongsToPatientAsync(Guid bookingId, Guid patientId, CancellationToken ct) =>
        db.Bookings.AnyAsync(b => b.BookingId == bookingId && b.PatientId == patientId, ct);

    private IActionResult ServeFile(string storedUrl, string fileName, string? contentType)
    {
        var path = storage.ResolvePath(storedUrl);
        if (path is null) return NotFound(new { message = "File is not available." });

        // Only ever serve a type from the upload allow-list; anything else is opaque bytes.
        var type = contentType is not null && FileStorageOptions.AllowedContentTypes.Contains(contentType)
            ? contentType
            : "application/octet-stream";

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(path, type, fileName, enableRangeProcessing: false);
    }
}
