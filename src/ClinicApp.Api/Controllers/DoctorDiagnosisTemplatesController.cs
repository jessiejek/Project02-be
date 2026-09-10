using System.Security.Claims;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record DiagnosisTemplateInput(string Label, string Body);

/// <summary>doctor_diagnosis_templates (§16.8) — a doctor's reusable free-text
/// diagnoses, picked into the consultation Diagnosis section and managed on
/// /doctor/templates. Always scoped to the caller's own doctor row; an Admin
/// may pass ?doctorId= to read/act on a specific doctor's list.</summary>
[ApiController]
[Authorize(Roles = "Doctor,Admin")]
[Route("api/doctor-diagnosis-templates")]
public class DoctorDiagnosisTemplatesController(ClinicAppDbContext db) : ControllerBase
{
    /// <summary>The doctor_id this request acts on. Doctor callers are always
    /// pinned to their own; Admin callers may target one via ?doctorId=.</summary>
    private async Task<Guid?> ResolveDoctorIdAsync(Guid? doctorIdQuery, CancellationToken ct)
    {
        if (User.IsInRole("Doctor") && !User.IsInRole("Admin"))
        {
            var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(sub, out var uid)) return null;
            return await db.StaffAccounts
                .Where(s => s.UserId == uid)
                .Select(s => (Guid?)s.StaffId)
                .SingleOrDefaultAsync(ct);
        }
        return doctorIdQuery; // Admin
    }

    [HttpGet]
    public async Task<ActionResult<List<DoctorDiagnosisTemplate>>> GetAll([FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var did = await ResolveDoctorIdAsync(doctorId, ct);
        if (did is null) return User.IsInRole("Doctor") ? Forbid() : Ok(new List<DoctorDiagnosisTemplate>());
        return Ok(await db.DoctorDiagnosisTemplates.AsNoTracking()
            .Where(t => t.DoctorId == did)
            .OrderBy(t => t.Label)
            .ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<DoctorDiagnosisTemplate>> Create([FromQuery] Guid? doctorId, DiagnosisTemplateInput input, CancellationToken ct)
    {
        var did = await ResolveDoctorIdAsync(doctorId, ct);
        if (did is null) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Label) || string.IsNullOrWhiteSpace(input.Body))
            return BadRequest(new { message = "Label and body are required." });

        var now = DateTimeOffset.UtcNow;
        var t = new DoctorDiagnosisTemplate
        {
            Id = Guid.NewGuid(),
            DoctorId = did.Value,
            Label = input.Label.Trim(),
            Body = input.Body.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.DoctorDiagnosisTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DoctorDiagnosisTemplate>> Update(Guid id, [FromQuery] Guid? doctorId, DiagnosisTemplateInput input, CancellationToken ct)
    {
        var did = await ResolveDoctorIdAsync(doctorId, ct);
        var t = await db.DoctorDiagnosisTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        if (User.IsInRole("Doctor") && !User.IsInRole("Admin") && t.DoctorId != did) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Label) || string.IsNullOrWhiteSpace(input.Body))
            return BadRequest(new { message = "Label and body are required." });

        t.Label = input.Label.Trim();
        t.Body = input.Body.Trim();
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var did = await ResolveDoctorIdAsync(doctorId, ct);
        var t = await db.DoctorDiagnosisTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        if (User.IsInRole("Doctor") && !User.IsInRole("Admin") && t.DoctorId != did) return Forbid();

        db.DoctorDiagnosisTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
