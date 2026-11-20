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
        //JJQR - AI (Nov 20, 2026) B3
        var query = db.Icd10Codes.AsNoTracking();
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(await query
                .OrderByDescending(c => c.IsLeaf)
                .ThenBy(c => c.Code)
                .Take(20)
                .ToListAsync(ct));
        }

        var s = q.Trim();
        var looksLikeCode = s.Length > 0 && char.IsLetter(s[0]) && s.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '.')
                            && s.Any(char.IsDigit);
        var qCode = s.Replace(".", "", StringComparison.Ordinal).ToUpperInvariant();
        var words = s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IQueryable<Icd10Code> filtered = query;
        if (looksLikeCode)
        {
            var letter = char.ToUpperInvariant(s[0]).ToString();
            filtered = query.Where(c => c.Code.StartsWith(letter));
        }
        else
        {
            foreach (var w in words)
            {
                var needle = w.ToLower();
                filtered = filtered.Where(c => c.Description.ToLower().Contains(needle));
            }
        }

        var rows = await filtered.ToListAsync(ct);
        var ranked = rows
            .Select(c => (c, rank: RankIcd(c, s, qCode, looksLikeCode)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenByDescending(x => x.c.IsLeaf)
            .ThenBy(x => x.c.Code, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(x => x.c)
            .ToList();
        return Ok(ranked);
        //JJQR - END
    }

    //JJQR - AI (Nov 20, 2026) B3
    private static int RankIcd(Icd10Code c, string q, string qCode, bool looksLikeCode)
    {
        var codeNoDot = c.Code.Replace(".", "", StringComparison.Ordinal);
        if (codeNoDot.Equals(qCode, StringComparison.OrdinalIgnoreCase)
            || c.Code.Equals(q, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (looksLikeCode && codeNoDot.StartsWith(qCode, StringComparison.OrdinalIgnoreCase))
            return 1;
        var words = q.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var desc = c.Description;
        var allWords = words.Length > 0 && words.All(w => desc.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (!looksLikeCode && !allWords && !c.Code.Contains(q, StringComparison.OrdinalIgnoreCase))
            return -1;
        if (looksLikeCode && !codeNoDot.StartsWith(qCode, StringComparison.OrdinalIgnoreCase) && !allWords)
            return -1;
        if (desc.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (allWords || c.Code.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 3;
        return -1;
    }
    //JJQR - END
}
