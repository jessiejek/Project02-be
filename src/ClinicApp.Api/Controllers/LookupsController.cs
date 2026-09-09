using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>
/// Clinical lookup tables (contract §4). Read-only, small, mostly static — the
/// Rx / vitals / diagnosis UIs load these. Public: the consultation pages that
/// use them are already role-gated; no per-row authorization applies.
/// </summary>
[ApiController]
public class LookupsController(ClinicAppDbContext db) : ControllerBase
{
    /// <summary>Rx medicine autocomplete catalog.</summary>
    [HttpGet("api/medicines")]
    public async Task<ActionResult<List<Medicine>>> GetMedicines([FromQuery] string? q, CancellationToken ct)
    {
        var query = db.Medicines.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(m => m.GenericName.Contains(q.Trim()));
        return Ok(await query.OrderBy(m => m.GenericName).ToListAsync(ct));
    }

    /// <summary>Vitals form field templates (7 defaults + any doctor-added customs).</summary>
    [HttpGet("api/vital-field-templates")]
    public async Task<ActionResult<List<VitalFieldTemplate>>> GetVitalFieldTemplates(CancellationToken ct) =>
        Ok(await db.VitalFieldTemplates.AsNoTracking().OrderBy(v => v.Description).ToListAsync(ct));

    /// <summary>§16.8 Form 3 — fixed lab-request panel (9 pre-printed + add-ons).</summary>
    [HttpGet("api/lab-test-catalog")]
    public async Task<ActionResult<List<LabTestCatalog>>> GetLabTestCatalog(CancellationToken ct) =>
        Ok(await db.LabTestCatalog.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct));

    /// <summary>ICD-10 code search (contract §15 gap — no FE caller today; the
    /// Phase 5 diagnosis picker will use it).</summary>
    [HttpGet("api/icd10-codes")]
    public async Task<ActionResult<List<Icd10Code>>> GetIcd10Codes([FromQuery] string? q, CancellationToken ct)
    {
        var query = db.Icd10Codes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.Code.Contains(s) || c.Description.Contains(s));
        }
        return Ok(await query.OrderBy(c => c.Code).Take(50).ToListAsync(ct));
    }
}
