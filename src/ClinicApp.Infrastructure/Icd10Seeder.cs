using System.Text;
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Loads the verbatim WHO ICD-10 2019 codes file into <c>icd10_codes</c>.
/// Idempotent: remaps US ICD-10-CM leftovers first, then bulk-upserts the catalog.
/// </summary>
//JJQR - AI (Nov 20, 2026) B1/B2
public static class Icd10Seeder
{
    public const int WhoCodeCount = 12221;
    private const string ResourceName = "ClinicApp.Infrastructure.Data.Icd10Who2019.icd102019syst_codes.txt";

    public static async Task SeedAsync(ClinicAppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var who = ReadWhoCodes();
        if (who.Count != WhoCodeCount)
            throw new InvalidOperationException($"WHO ICD-10 file parsed {who.Count} rows; expected {WhoCodeCount}.");

        await RemapSavedDataAsync(db, logger, ct);

        var count = await db.Icd10Codes.CountAsync(ct);
        if (count == who.Count)
        {
            var sample = await db.Icd10Codes.AsNoTracking().FirstOrDefaultAsync(c => c.Code == "J45.9", ct);
            var hasStale = await db.Icd10Codes.AsNoTracking().AnyAsync(c => c.Code == "J45.909", ct);
            if (sample is { Description: "Asthma, unspecified", Level: 4, IsLeaf: true } && !hasStale)
            {
                logger.LogInformation("Icd10Seeder: icd10_codes already has {Count} WHO rows; skipping catalog upsert.", count);
                return;
            }
        }

        var whoByCode = who.ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
        var existing = await db.Icd10Codes.ToListAsync(ct);
        var existingCodes = existing.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<Icd10Code>();
        foreach (var row in who)
        {
            if (existingCodes.Contains(row.Code)) continue;
            toAdd.Add(new Icd10Code
            {
                Code = row.Code,
                Description = row.Description,
                Level = row.Level,
                IsLeaf = row.IsLeaf,
            });
        }

        foreach (var row in existing)
        {
            if (!whoByCode.TryGetValue(row.Code, out var want)) continue;
            if (row.Description != want.Description || row.Level != want.Level || row.IsLeaf != want.IsLeaf)
            {
                row.Description = want.Description;
                row.Level = want.Level;
                row.IsLeaf = want.IsLeaf;
            }
        }

        if (toAdd.Count > 0) db.Icd10Codes.AddRange(toAdd);

        var extras = existing.Where(c => !whoByCode.ContainsKey(c.Code)).ToList();
        if (extras.Count > 0) db.Icd10Codes.RemoveRange(extras);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Icd10Seeder: catalog upsert — added {Added}, removed {Removed} non-WHO, total {Total}.",
            toAdd.Count, extras.Count, who.Count);
    }

    internal static IReadOnlyList<WhoRow> ReadWhoCodes()
    {
        var asm = typeof(Icd10Seeder).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Missing embedded ICD-10 resource '{ResourceName}'. Have: {string.Join(", ", asm.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rows = new List<WhoRow>(WhoCodeCount);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            var fields = line.Split(';');
            if (fields.Length < 9) continue;
            var code = fields[6].Trim();
            if (code.Length == 0) continue;
            _ = int.TryParse(fields[0], out var level);
            var isLeaf = string.Equals(fields[1], "T", StringComparison.OrdinalIgnoreCase);
            rows.Add(new WhoRow(code, fields[8], level, isLeaf));
        }
        return rows;
    }

    private static async Task RemapSavedDataAsync(ClinicAppDbContext db, ILogger logger, CancellationToken ct)
    {
        var nDx = await RemapConsultationDiagnosesAsync(db, ct);
        var nTpl = await RemapDoctorDiagnosisTemplatesAsync(db, ct);
        var nMcTpl = await RemapMedicalCertificateTemplatesAsync(db, ct);
        var nMc = await RemapMedicalCertificatesAsync(db, ct);
        var nSoap = await RemapSoapTemplatesAsync(db, ct);
        var nPhrase = await RemapSoapPhrasesAsync(db, ct);

        if (nDx + nTpl + nMcTpl + nMc + nSoap + nPhrase > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation("Icd10Seeder: remapped {Count} row(s) in {Table}.", nDx, "consultation_diagnoses");
        logger.LogInformation("Icd10Seeder: remapped {Count} row(s) in {Table}.", nTpl, "doctor_diagnosis_templates");
        logger.LogInformation("Icd10Seeder: remapped {Count} row(s) in {Table}.", nMcTpl, "medical_certificate_templates");
        logger.LogInformation("Icd10Seeder: remapped {Count} row(s) in {Table}.", nMc, "medical_certificates");
        logger.LogInformation("Icd10Seeder: remapped {Count} row(s) in {Table}.", nSoap, "soap_templates");
        logger.LogInformation("Icd10Seeder: remapped {Count} row(s) in {Table}.", nPhrase, "soap_phrases");
    }

    private static async Task<int> RemapConsultationDiagnosesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var usCodes = Icd10UsToWho.Entries.Select(e => e.UsCode).ToArray();
        var usDescs = Icd10UsToWho.Entries.Select(e => e.UsDescription).ToArray();
        var rows = await db.ConsultationDiagnoses
            .Where(d => (d.Icd10Code != null && usCodes.Contains(d.Icd10Code))
                        || (d.CustomDescription != null && usDescs.Contains(d.CustomDescription)))
            .ToListAsync(ct);
        var n = 0;
        foreach (var row in rows)
        {
            var changed = RemapCodeAndDescription(row.Icd10Code, row.CustomDescription, out var code, out var desc);
            var newDesc = Icd10UsToWho.ReplaceCodesInText(desc);
            if (newDesc != (desc ?? "")) { desc = newDesc; changed = true; }
            if (!changed) continue;
            row.Icd10Code = code;
            row.CustomDescription = desc;
            n++;
        }
        return n;
    }

    private static async Task<int> RemapDoctorDiagnosisTemplatesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var n = 0;
        foreach (var row in await db.DoctorDiagnosisTemplates.ToListAsync(ct))
        {
            var changed = RemapCodeAndDescription(row.Icd10Code, row.Body, out var code, out var body);
            var label = Icd10UsToWho.ReplaceCodesInText(row.Label);
            var bodyText = Icd10UsToWho.ReplaceCodesInText(body);
            if (label != row.Label || bodyText != (body ?? "") || changed)
            {
                row.Icd10Code = code;
                row.Label = label;
                row.Body = bodyText;
                n++;
            }
        }
        return n;
    }

    private static async Task<int> RemapMedicalCertificateTemplatesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var n = 0;
        foreach (var row in await db.MedicalCertificateTemplates.ToListAsync(ct))
        {
            var title = Icd10UsToWho.ReplaceCodesInText(row.Title);
            var dx = Icd10UsToWho.ReplaceCodesInText(row.DiagnosisText);
            var recs = Icd10UsToWho.ReplaceCodesInText(row.Recommendations);
            var purpose = Icd10UsToWho.ReplaceCodesInText(row.PurposeException);
            if (title == row.Title && dx == (row.DiagnosisText ?? "") && recs == (row.Recommendations ?? "") && purpose == (row.PurposeException ?? ""))
                continue;
            row.Title = title;
            row.DiagnosisText = string.IsNullOrEmpty(dx) ? row.DiagnosisText : dx;
            row.Recommendations = string.IsNullOrEmpty(recs) ? row.Recommendations : recs;
            row.PurposeException = string.IsNullOrEmpty(purpose) ? row.PurposeException : purpose;
            n++;
        }
        return n;
    }

    private static async Task<int> RemapMedicalCertificatesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var rows = await db.MedicalCertificates
            .Where(m => m.DiagnosisText != null && (
                m.DiagnosisText.Contains("J45.909") || m.DiagnosisText.Contains("Z00.129") ||
                m.DiagnosisText.Contains("O24.419") || m.DiagnosisText.Contains("O99.210") ||
                m.DiagnosisText.Contains("O14.90") || m.DiagnosisText.Contains("O13.9") ||
                m.DiagnosisText.Contains("A90") || m.DiagnosisText.Contains("A09") ||
                m.DiagnosisText.Contains("O80") || m.DiagnosisText.Contains("O82")))
            .ToListAsync(ct);
        var n = 0;
        foreach (var row in rows)
        {
            var dx = Icd10UsToWho.ReplaceCodesInText(row.DiagnosisText);
            if (dx == (row.DiagnosisText ?? "")) continue;
            row.DiagnosisText = dx;
            n++;
        }
        return n;
    }

    private static async Task<int> RemapSoapTemplatesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var n = 0;
        foreach (var row in await db.SoapTemplates.ToListAsync(ct))
        {
            var title = Icd10UsToWho.ReplaceCodesInText(row.Title);
            var cc = Icd10UsToWho.ReplaceCodesInText(row.ChiefComplaint);
            var sub = Icd10UsToWho.ReplaceCodesInText(row.Subjective);
            var obj = Icd10UsToWho.ReplaceCodesInText(row.Objective);
            var assess = Icd10UsToWho.ReplaceCodesInText(row.Assessment);
            var plan = Icd10UsToWho.ReplaceCodesInText(row.Plan);
            if (title == row.Title && cc == (row.ChiefComplaint ?? "") && sub == (row.Subjective ?? "")
                && obj == (row.Objective ?? "") && assess == (row.Assessment ?? "") && plan == (row.Plan ?? ""))
                continue;
            row.Title = title;
            row.ChiefComplaint = cc;
            row.Subjective = sub;
            row.Objective = obj;
            row.Assessment = assess;
            row.Plan = plan;
            n++;
        }
        return n;
    }

    private static async Task<int> RemapSoapPhrasesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var n = 0;
        foreach (var row in await db.SoapPhrases.ToListAsync(ct))
        {
            var label = Icd10UsToWho.ReplaceCodesInText(row.Label);
            var body = Icd10UsToWho.ReplaceCodesInText(row.Body);
            if (label == row.Label && body == row.Body) continue;
            row.Label = label;
            row.Body = body;
            n++;
        }
        return n;
    }

    private static bool RemapCodeAndDescription(string? code, string? description, out string? newCode, out string? newDescription)
    {
        newCode = code;
        newDescription = description;
        var changed = false;
        if (Icd10UsToWho.TryMapCode(code, out var entry))
        {
            newCode = entry.WhoCode;
            changed = true;
            if (Icd10UsToWho.TryReplaceUsDescription(description, entry.WhoDescription, entry.UsDescription, out var replaced))
                newDescription = replaced;
        }
        else
        {
            foreach (var e in Icd10UsToWho.Entries)
            {
                if (Icd10UsToWho.TryReplaceUsDescription(description, e.WhoDescription, e.UsDescription, out var replaced))
                {
                    newDescription = replaced;
                    changed = true;
                    break;
                }
            }
        }
        return changed;
    }

    internal readonly record struct WhoRow(string Code, string Description, int Level, bool IsLeaf);
}
//JJQR - END
