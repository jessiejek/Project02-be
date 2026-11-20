using ClinicApp.Domain.Enums;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Default diagnoses and SOAP phrases that used to live only in <see cref="DevDataSeeder"/>.
/// Prod GET/restore uses the same list; keys are stable (the historical seed Guids).
/// </summary>
//JJQR - AI (Nov 20, 2026) A2
public static class BuiltInTemplateKind
{
    public const string Soap = "soap";
    public const string Rx = "rx";
    public const string Cert = "cert";
    public const string Diagnosis = "diagnosis";
    public const string Phrase = "phrase";
    public const string Favorite = "favorite";

    public static readonly string[] Copyable = [Soap, Rx, Cert, Diagnosis, Phrase];

    public static string? Normalize(string? kind)
    {
        var k = kind?.Trim().ToLowerInvariant();
        return k is Soap or Rx or Cert or Diagnosis or Phrase or Favorite ? k : null;
    }

    public static bool IsCopyable(string? kind) =>
        kind is Soap or Rx or Cert or Diagnosis or Phrase;
}

public sealed record BuiltInDiagnosisDef(string Key, string Label, string Body);
public sealed record BuiltInPhraseDef(string Key, SoapField Field, string Label, string Body);

public static class BuiltInTemplateCatalog
{
    private static Guid G(string s) => Guid.Parse(s);
    private static string K(string s) => G(s).ToString();

    public static readonly BuiltInDiagnosisDef[] Diagnoses =
    [
        new(K("d1111111-1111-4111-8111-111111111101"), "J06.9 — Acute URI",
            "Acute upper respiratory infection, likely viral; no signs of lower tract involvement."),
        new(K("d1111111-1111-4111-8111-111111111102"), "I10 — Essential hypertension",
            "Essential (primary) hypertension; blood pressure at goal on current regimen."),
        new(K("d1111111-1111-4111-8111-111111111103"), "E11.9 — Type 2 DM",
            "Type 2 diabetes mellitus without complications; glycemic control per recent labs."),
        //JJQR - AI (Nov 20, 2026) B2 — A09 (US unspecified) -> WHO A09.0
        new(K("d1111111-1111-4111-8111-111111111104"), "A09.0 — Other and unspecified gastroenteritis and colitis of infectious origin",
            "Acute infectious gastroenteritis, presumed viral; mild dehydration."),
        new(K("d1111111-1111-4111-8111-111111111105"), "N39.0 — UTI",
            "Urinary tract infection, site not specified; uncomplicated cystitis."),
        new(K("d1111111-1111-4111-8111-111111111106"), "Z00.0 — General adult exam",
            "General adult medical examination without abnormal findings."),
        new(K("d1111111-1111-4111-8111-111111111107"), "R50.9 — Fever",
            "Fever, unspecified; no clear focus on initial evaluation."),
        new(K("d1111111-1111-4111-8111-111111111108"), "Z34.9 — Supervision of pregnancy",
            "Supervision of normal pregnancy; routine prenatal care."),
        new(K("d1111111-1111-4111-8111-111111111109"), "J02.9 — Acute pharyngitis",
            "Acute pharyngitis, unspecified; likely viral etiology."),
        new(K("d1111111-1111-4111-8111-111111111110"), "K21.9 — GERD",
            "Gastro-esophageal reflux disease without esophagitis; symptoms controlled."),
    ];

    public static readonly BuiltInPhraseDef[] Phrases =
    [
        new(K("e1111111-1111-4111-8111-111111111101"), SoapField.ChiefComplaint, "Cough and colds", "Cough, runny nose, and sore throat for several days."),
        new(K("e1111111-1111-4111-8111-111111111102"), SoapField.ChiefComplaint, "Fever", "Intermittent fever with body malaise for 2 days."),
        new(K("e1111111-1111-4111-8111-111111111103"), SoapField.Subjective, "Denies red flags", "Denies dyspnea, chest pain, hemoptysis, or altered sensorium."),
        new(K("e1111111-1111-4111-8111-111111111104"), SoapField.Subjective, "No sick contacts", "No known sick contacts; no recent travel."),
        new(K("e1111111-1111-4111-8111-111111111105"), SoapField.Subjective, "Symptom onset", "Symptoms began gradually about 3 days ago."),
        new(K("e1111111-1111-4111-8111-111111111106"), SoapField.Subjective, "Hydration OK", "Able to eat and drink; urine output normal."),
        new(K("e1111111-1111-4111-8111-111111111107"), SoapField.Objective, "Lungs clear", "Lungs clear to auscultation bilaterally; no wheezes or crackles."),
        new(K("e1111111-1111-4111-8111-111111111108"), SoapField.Objective, "Throat exam", "Oropharynx mildly erythematous; no tonsillar exudates."),
        new(K("e1111111-1111-4111-8111-111111111109"), SoapField.Objective, "Stable vitals", "Alert, not in distress; vital signs stable for age."),
        new(K("e1111111-1111-4111-8111-111111111110"), SoapField.Objective, "No lymphadenopathy", "No significant cervical lymphadenopathy."),
        new(K("e1111111-1111-4111-8111-111111111111"), SoapField.Assessment, "Viral URI", "Viral upper respiratory infection, likely self-limited."),
        new(K("e1111111-1111-4111-8111-111111111112"), SoapField.Assessment, "HTN controlled", "Essential hypertension, controlled on current therapy."),
        new(K("e1111111-1111-4111-8111-111111111113"), SoapField.Plan, "Symptomatic care", "Symptomatic care: fluids, rest, paracetamol PRN."),
        new(K("e1111111-1111-4111-8111-111111111114"), SoapField.Plan, "Return precautions", "Return if fever persists >3 days, worsening breathlessness, or new red flags."),
        new(K("e1111111-1111-4111-8111-111111111115"), SoapField.Plan, "Continue meds", "Continue present medications; routine follow-up as scheduled."),
    ];
}
//JJQR - END
