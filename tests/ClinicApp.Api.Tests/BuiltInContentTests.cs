using System.Text.RegularExpressions;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 21, 2026) - the templates, phrases, certificates and medicines the app ships with must stay correct: right section, no pre-asserted findings, no leftover placeholders, no doses
/// <summary>Content rules found in the template audit, enforced so they cannot come back.</summary>
public class BuiltInContentRuleTests
{
    private static readonly Regex Placeholder = new(@"___|\[|\]|\bstated\b|when applicable|when available|when checked|\bdocumented\b|prescribed number|\bTBD\b", RegexOptions.IgnoreCase);
    private static readonly Regex Dose = new(@"\b\d+(\.\d+)?\s?(mg|mcg|ml|g|iu)\b", RegexOptions.IgnoreCase);

    private static IEnumerable<string> AllText()
    {
        foreach (var t in BuiltInTemplateCatalog.SoapTemplates)
            foreach (var f in new[] { t.Title, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan }) yield return f;
        foreach (var c in BuiltInTemplateCatalog.CertTemplates)
            foreach (var f in new[] { c.Title, c.DiagnosisText ?? "", c.Recommendations, c.PurposeException }) yield return f;
        foreach (var p in BuiltInTemplateCatalog.Phrases) { yield return p.Label; yield return p.Body; }
    }

    [Fact]
    public void There_are_templates_for_the_common_adult_and_child_conditions()
    {
        var titles = BuiltInTemplateCatalog.SoapTemplates.Select(t => t.Title).ToList();
        foreach (var must in new[] { "Upper respiratory infection", "Acute gastroenteritis", "Uncomplicated UTI", "Hypertension follow-up", "Type 2 diabetes follow-up",
                                     "Community-acquired pneumonia", "Pediatric pneumonia", "Skin and soft-tissue infection", "Asthma follow-up", "Fever work-up" })
            Assert.Contains(must, titles);
        Assert.True(titles.Count >= 20);
    }

    [Fact]
    public void Keys_and_titles_are_unique_and_every_key_is_a_guid()
    {
        var keys = BuiltInTemplateCatalog.SoapTemplates.Select(t => t.Key)
            .Concat(BuiltInTemplateCatalog.CertTemplates.Select(c => c.Key))
            .Concat(BuiltInTemplateCatalog.Phrases.Select(p => p.Key)).ToList();
        Assert.All(keys, k => Assert.True(Guid.TryParse(k, out _), k));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(BuiltInTemplateCatalog.SoapTemplates.Length, BuiltInTemplateCatalog.SoapTemplates.Select(t => t.Title.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(BuiltInTemplateCatalog.CertTemplates.Length, BuiltInTemplateCatalog.CertTemplates.Select(t => t.Title.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void No_leftover_placeholder_or_filler_text_anywhere()
    {
        foreach (var text in AllText())
            Assert.False(Placeholder.IsMatch(text), $"placeholder / filler in: {text}");
    }

    [Fact]
    public void Every_soap_field_is_filled_and_each_sits_in_its_own_section()
    {
        foreach (var t in BuiltInTemplateCatalog.SoapTemplates)
        {
            foreach (var f in new[] { t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan })
                Assert.False(string.IsNullOrWhiteSpace(f), $"{t.Title}: empty field");
            Assert.True(t.ChiefComplaint.Length <= 80 && !t.ChiefComplaint.Contains('\n'), $"{t.Title}: chief complaint must be the short reason for the visit");
            Assert.True(t.Assessment.Length <= 120 && !t.Assessment.Contains('\n'), $"{t.Title}: assessment must be a short diagnosis, not advice");
            Assert.DoesNotMatch(@"(?i)\b(reported|denies|complains|states)\b", t.Objective);          // what the patient says belongs in Subjective
            Assert.DoesNotMatch(@"(?i)\b(return|continue|advise|follow-up)\b", t.Assessment);        // advice belongs in Plan
            Assert.DoesNotMatch(@"(?i)\b(blood pressure|bp|pulse)\s+(is|was|within|normal|elevated|stable)\b", t.Objective); // no pre-asserted measurements
            Assert.Matches("(?i)return", t.Plan);                                                    // every plan says when to come back
            Assert.False(Dose.IsMatch(t.Plan), $"{t.Title}: no doses in a template plan — the doctor prescribes");
        }
    }

    [Fact]
    public void Certificates_do_not_assume_a_diagnosis_or_a_fever()
    {
        foreach (var c in BuiltInTemplateCatalog.CertTemplates)
        {
            Assert.DoesNotMatch(@"(?i)febrile|fever|afebrile|contagious", c.DiagnosisText ?? "");
            Assert.DoesNotMatch(@"(?i)febrile|fever|afebrile", c.Recommendations);
            Assert.False(string.IsNullOrWhiteSpace(c.Recommendations));
            Assert.False(string.IsNullOrWhiteSpace(c.PurposeException));
        }
        Assert.Null(BuiltInTemplateCatalog.CertTemplates.Single(c => c.Title == "Rest days (3 days)").DiagnosisText);
    }

    [Fact]
    public void Phrases_cover_all_five_sections_with_unique_labels_per_section_and_none_pre_asserts_vitals()
    {
        var byField = BuiltInTemplateCatalog.Phrases.GroupBy(p => p.Field).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var f in Enum.GetValues<SoapField>())
        {
            Assert.True(byField.ContainsKey(f) && byField[f].Count >= 5, $"{f} has too few phrases");
            Assert.Equal(byField[f].Count, byField[f].Select(p => p.Label.ToLowerInvariant()).Distinct().Count());
        }
        Assert.All(byField[SoapField.ChiefComplaint], p => Assert.True(p.Body.Length <= 60, p.Body));          // a reason for the visit, not a paragraph
        Assert.All(byField[SoapField.Objective], p => Assert.DoesNotMatch(@"(?i)\bdenies\b|\breports?\b", p.Body));
        Assert.DoesNotContain(BuiltInTemplateCatalog.Phrases, p => p.Body.Contains("vital signs stable", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The medicine list and the copy-to-the-doctor step, against the real (test) database.</summary>
[Collection("api")]
public class BuiltInContentFlowTests(ApiFixture api) : ApiTestBase(api)
{
    [Fact]
    public async Task The_medicine_catalog_has_the_everyday_formulary_medicines_and_no_duplicates()
    {
        var names = await WithDb(db => db.Medicines.AsNoTracking().Select(m => m.GenericName).ToListAsync());
        foreach (var must in MedicineCatalogSeeder.GuaranteedNames) Assert.Contains(must, names);
        Assert.Equal(names.Count, names.Select(n => n.Trim().ToUpperInvariant()).Distinct().Count());
        Assert.Contains("AMOXICILLIN 500MG CAP", names);               // the most common antibiotic was missing before
        Assert.Contains("CEFIXIME 200MG CAP", names);                  // formulary lists capsules, not tablets
        Assert.DoesNotContain(names, n => n.Contains("MULTIVITAMINS")); // not a defined product
        Assert.All(MedicineCatalogSeeder.GuaranteedNames, n => Assert.Equal(n.ToUpperInvariant(), n)); // shipped names: generic only, one consistent style
    }

    [Fact]
    public async Task Seeding_the_medicines_twice_changes_nothing()
    {
        var changed = await WithDb(async db => await MedicineCatalogSeeder.SeedAsync(db, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task A_doctor_gets_every_code_defined_soap_and_certificate_template_once()
    {
        var doctor = W.DoctorB.RowId;
        foreach (var kind in new[] { BuiltInTemplateKind.Soap, BuiltInTemplateKind.Cert })
        {
            await WithDb(async db => { await new BuiltInTemplateCopier(db).EnsureCopiesAsync(doctor, kind); return 0; });
            await WithDb(async db => { await new BuiltInTemplateCopier(db).EnsureCopiesAsync(doctor, kind); return 0; }); // idempotent
        }
        var soap = await WithDb(db => db.SoapTemplates.AsNoTracking().Where(t => t.DoctorId == doctor && !t.IsSystemTemplate && t.SourceBuiltinKey != null).Select(t => t.SourceBuiltinKey!).ToListAsync());
        var certs = await WithDb(db => db.MedicalCertificateTemplates.AsNoTracking().Where(t => t.DoctorId == doctor && !t.IsSystemTemplate && t.SourceBuiltinKey != null).Select(t => t.SourceBuiltinKey!).ToListAsync());
        foreach (var d in BuiltInTemplateCatalog.SoapTemplates) Assert.Single(soap, k => string.Equals(k, d.Key, StringComparison.OrdinalIgnoreCase));
        foreach (var d in BuiltInTemplateCatalog.CertTemplates) Assert.Single(certs, k => string.Equals(k, d.Key, StringComparison.OrdinalIgnoreCase));
    }
}
//JJQR - END
