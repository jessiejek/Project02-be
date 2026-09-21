using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record SoapPhraseInput(SoapField Field, string Label, string Body);
public record SoapTemplateInput(string Title, bool IsSystemTemplate, string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan);

/// <summary>soap_phrases / soap_templates (contract §4) — doctor SOAP quick-insert tooling.</summary>
[ApiController]
[Authorize(Roles = "Doctor,Admin")]
[Route("api")]
public class SoapController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet("soap-phrases")]
    public async Task<ActionResult<List<SoapPhrase>>> GetPhrases([FromQuery] Guid doctorId, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        return Ok(await db.SoapPhrases.AsNoTracking().Where(p => p.DoctorId == doctorId).ToListAsync(ct));
    }

    [HttpPost("soap-phrases")]
    public async Task<ActionResult<SoapPhrase>> CreatePhrase([FromQuery] Guid doctorId, SoapPhraseInput input, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        var now = DateTimeOffset.UtcNow;
        var p = new SoapPhrase
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, Field = input.Field,
            Label = input.Label, Body = input.Body, CreatedAt = now, UpdatedAt = now
        };
        db.SoapPhrases.Add(p);
        await db.SaveChangesAsync(ct);
        return Ok(p);
    }

    [HttpPut("soap-phrases/{id:guid}")]
    public async Task<ActionResult<SoapPhrase>> UpdatePhrase(Guid id, SoapPhraseInput input, CancellationToken ct)
    {
        var p = await db.SoapPhrases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(p.DoctorId)) return Forbid();
        p.Field = input.Field;
        p.Label = input.Label;
        p.Body = input.Body;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(p);
    }

    [HttpDelete("soap-phrases/{id:guid}")]
    public async Task<IActionResult> DeletePhrase(Guid id, CancellationToken ct)
    {
        var p = await db.SoapPhrases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(p.DoctorId)) return Forbid();
        db.SoapPhrases.Remove(p);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("soap-templates")]
    public async Task<ActionResult<List<SoapTemplate>>> GetTemplates([FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor)
        {
            // Own templates + system templates only, never another doctor's.
            if (doctorId is not null && !actor.ActsAsDoctor(doctorId.Value)) return Forbid();
            doctorId = actor.StaffId;
        }

        var q = db.SoapTemplates.AsNoTracking().AsQueryable();
        if (doctorId is not null) q = q.Where(t => t.DoctorId == doctorId || t.IsSystemTemplate);
        return Ok(await q.OrderBy(t => t.Title).ToListAsync(ct));
    }

    [HttpPost("soap-templates")]
    public async Task<ActionResult<SoapTemplate>> CreateTemplate([FromQuery] Guid doctorId, SoapTemplateInput input, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(doctorId)) return Forbid();
        if (input.IsSystemTemplate && !actor.IsAdmin) return Forbid(); // system templates are Admin-managed
        var now = DateTimeOffset.UtcNow;
        var t = new SoapTemplate
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, Title = input.Title, IsSystemTemplate = input.IsSystemTemplate,
            ChiefComplaint = input.ChiefComplaint, Subjective = input.Subjective, Objective = input.Objective,
            Assessment = input.Assessment, Plan = input.Plan, CreatedAt = now, UpdatedAt = now
        };
        db.SoapTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpPut("soap-templates/{id:guid}")]
    public async Task<ActionResult<SoapTemplate>> UpdateTemplate(Guid id, SoapTemplateInput input, CancellationToken ct)
    {
        var t = await db.SoapTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(t.DoctorId) || ((t.IsSystemTemplate || input.IsSystemTemplate) && !actor.IsAdmin)) return Forbid();
        t.Title = input.Title;
        t.IsSystemTemplate = input.IsSystemTemplate;
        t.ChiefComplaint = input.ChiefComplaint;
        t.Subjective = input.Subjective;
        t.Objective = input.Objective;
        t.Assessment = input.Assessment;
        t.Plan = input.Plan;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(t);
    }

    [HttpDelete("soap-templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        var t = await db.SoapTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(t.DoctorId) || (t.IsSystemTemplate && !actor.IsAdmin)) return Forbid();
        db.SoapTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
