using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record SoapPhraseInput(SoapField Field, string Label, string Body);
//JJQR - AI (Nov 19, 2026) - ids in the order the doctor wants them (phrases / favorites reorder)
public record OrderInput(List<Guid> Ids);
//JJQR - END
public record SoapTemplateInput(string Title, bool IsSystemTemplate, string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan);

/// <summary>soap_phrases / soap_templates (contract §4) — doctor SOAP quick-insert tooling.</summary>
[ApiController]
[Authorize(Roles = "Doctor,Admin")]
[Route("api")]
public class SoapController(ClinicAppDbContext db, ActorResolver actors, BuiltInTemplateCopier copier) : ControllerBase
{
    [HttpGet("soap-phrases")]
    public async Task<ActionResult<List<SoapPhrase>>> GetPhrases([FromQuery] Guid doctorId, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        //JJQR - AI (Nov 20, 2026) A2 — copy catalog phrases into her own rows, then list only hers
        await copier.EnsureCopiesAsync(doctorId, BuiltInTemplateKind.Phrase, ct);
        //JJQR - END
        //JJQR - AI (Nov 19, 2026) - doctor-chosen order first (sort_order), then label; JSON gains sort_order
        return Ok(await db.SoapPhrases.AsNoTracking().Where(p => p.DoctorId == doctorId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Label).ToListAsync(ct));
        //JJQR - END
    }

    //JJQR - AI (Nov 19, 2026) - phrase text rules (server side): non-blank label + body, sane lengths
    private ActionResult? ValidatePhrase(SoapPhraseInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Label) || string.IsNullOrWhiteSpace(input.Body))
            return BadRequest(new { message = "A phrase needs a name and text." });
        if (input.Label.Trim().Length > 200) return BadRequest(new { message = "Phrase name is too long (max 200)." });
        if (input.Body.Trim().Length > 4000) return BadRequest(new { message = "Phrase text is too long (max 4000)." });
        return null;
    }

    /// <summary>PUT /api/soap-phrases/order?doctorId= — body {ids:[…]} sets sort_order 1..n in that order.
    /// Every id must be one of the doctor's own phrases, otherwise 403 and nothing changes.</summary>
    [HttpPut("soap-phrases/order")]
    public async Task<IActionResult> ReorderPhrases([FromQuery] Guid doctorId, OrderInput input, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        var ids = (input.Ids ?? new()).Distinct().ToList();
        var rows = await db.SoapPhrases.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (rows.Count != ids.Count || rows.Any(p => p.DoctorId != doctorId)) return Forbid();
        for (var i = 0; i < ids.Count; i++) rows.Single(p => p.Id == ids[i]).SortOrder = i + 1;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    //JJQR - END

    [HttpPost("soap-phrases")]
    public async Task<ActionResult<SoapPhrase>> CreatePhrase([FromQuery] Guid doctorId, SoapPhraseInput input, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        //JJQR - AI (Nov 19, 2026) - validate + trim; new phrases go to the end of their field
        if (ValidatePhrase(input) is { } bad) return bad;
        var last = await db.SoapPhrases.Where(x => x.DoctorId == doctorId && x.Field == input.Field).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;
        //JJQR - END
        var now = DateTimeOffset.UtcNow;
        var p = new SoapPhrase
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, Field = input.Field,
            Label = input.Label.Trim(), Body = input.Body.Trim(), SortOrder = last + 1, CreatedAt = now, UpdatedAt = now
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
        //JJQR - AI (Nov 19, 2026) - validate + trim; moving to another field puts it at the end of that field
        if (ValidatePhrase(input) is { } bad) return bad;
        if (p.Field != input.Field)
            p.SortOrder = (await db.SoapPhrases.Where(x => x.DoctorId == p.DoctorId && x.Field == input.Field).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        //JJQR - END
        p.Field = input.Field;
        p.Label = input.Label.Trim();
        p.Body = input.Body.Trim();
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

        //JJQR - AI (Nov 20, 2026) A2 — doctor (or admin acting for her) gets her own copies, never system rows
        if (doctorId is not null && actor.ActsAsDoctor(doctorId.Value))
        {
            await copier.EnsureCopiesAsync(doctorId.Value, BuiltInTemplateKind.Soap, ct);
            return Ok(await db.SoapTemplates.AsNoTracking()
                .Where(t => t.DoctorId == doctorId && !t.IsSystemTemplate)
                .OrderBy(t => t.Title).ToListAsync(ct));
        }
        //JJQR - END

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
