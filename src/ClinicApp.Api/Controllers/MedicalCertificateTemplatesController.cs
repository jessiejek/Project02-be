using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record MedicalCertificateTemplateInput(
    string Title, bool IsSystemTemplate, string? DiagnosisText, string? Recommendations, string? PurposeException);

/// <summary>medical_certificate_templates — canned "reason for the cert" text a
/// doctor reuses across §16.8 Form 2 issuances. Same shape/permissions as
/// soap-templates.</summary>
[ApiController]
[Authorize(Roles = "Doctor,Admin")]
[Route("api/medical-certificate-templates")]
public class MedicalCertificateTemplatesController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MedicalCertificateTemplate>>> GetAll([FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor)
        {
            if (doctorId is not null && !actor.ActsAsDoctor(doctorId.Value)) return Forbid();
            doctorId = actor.StaffId;
        }

        var q = db.MedicalCertificateTemplates.AsNoTracking().AsQueryable();
        if (doctorId is not null) q = q.Where(t => t.DoctorId == doctorId || t.IsSystemTemplate);
        return Ok(await q.OrderBy(t => t.Title).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<MedicalCertificateTemplate>> Create(
        [FromQuery] Guid doctorId, MedicalCertificateTemplateInput input, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(doctorId)) return Forbid();
        if (input.IsSystemTemplate && !actor.IsAdmin) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var t = new MedicalCertificateTemplate
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            Title = input.Title,
            IsSystemTemplate = input.IsSystemTemplate,
            DiagnosisText = input.DiagnosisText,
            Recommendations = input.Recommendations,
            PurposeException = input.PurposeException,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.MedicalCertificateTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MedicalCertificateTemplate>> Update(Guid id, MedicalCertificateTemplateInput input, CancellationToken ct)
    {
        var t = await db.MedicalCertificateTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(t.DoctorId) || ((t.IsSystemTemplate || input.IsSystemTemplate) && !actor.IsAdmin)) return Forbid();
        t.Title = input.Title;
        t.IsSystemTemplate = input.IsSystemTemplate;
        t.DiagnosisText = input.DiagnosisText;
        t.Recommendations = input.Recommendations;
        t.PurposeException = input.PurposeException;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await db.MedicalCertificateTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(t.DoctorId) || (t.IsSystemTemplate && !actor.IsAdmin)) return Forbid();
        db.MedicalCertificateTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
