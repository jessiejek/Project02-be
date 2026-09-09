using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicApp.Infrastructure.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>patient_documents / patient_lab_results (contract §4, §8). Multipart
/// upload → local disk (App_Data/uploads) → row insert; file_url points at the
/// static-file mount, same shape as the old Supabase public URL.</summary>
[ApiController]
[Authorize]
[Route("api")]
public class PatientFilesController(ClinicAppDbContext db, IFileStorageService storage) : ControllerBase
{
    // ── patient_documents ─────────────────────────────────────────────────
    [HttpGet("patient-documents")]
    public async Task<ActionResult<List<PatientDocument>>> GetDocuments(
        [FromQuery] Guid? patientId, [FromQuery] Guid? bookingId, CancellationToken ct)
    {
        var q = db.PatientDocuments.AsNoTracking()
            .Include(d => d.Booking).ThenInclude(b => b!.Doctor).ThenInclude(d => d!.StaffAccount).AsQueryable();
        if (patientId is not null) q = q.Where(d => d.PatientId == patientId);
        if (bookingId is not null) q = q.Where(d => d.BookingId == bookingId);
        return Ok(await q.OrderByDescending(d => d.UploadedAt).ToListAsync(ct));
    }

    [HttpPost("patient-documents")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<PatientDocument>> UploadDocument(
        [FromForm] Guid patientId, [FromForm] Guid bookingId, [FromForm] Guid? consultationId,
        [FromForm] string? title, [FromForm] string? description, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file." });

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
            UploadedByUserId = CurrentUserId(),
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
        var q = db.PatientLabResults.AsNoTracking()
            .Include(r => r.Booking).ThenInclude(b => b!.Doctor).ThenInclude(d => d!.StaffAccount).AsQueryable();
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        if (bookingId is not null) q = q.Where(r => r.BookingId == bookingId);
        return Ok(await q.OrderByDescending(r => r.UploadedAt).ToListAsync(ct));
    }

    [HttpPost("patient-lab-results")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<PatientLabResult>> UploadLabResult(
        [FromForm] Guid patientId, [FromForm] Guid bookingId, [FromForm] Guid? consultationId,
        [FromForm] Guid? labOrderId, [FromForm] string? resultTitle, [FromForm] string? resultText,
        IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file." });

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

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
