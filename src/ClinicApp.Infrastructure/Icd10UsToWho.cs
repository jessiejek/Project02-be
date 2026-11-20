using System.Text.RegularExpressions;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Single map of US ICD-10-CM codes previously stored in this app to WHO ICD-10 2019.
/// </summary>
//JJQR - AI (Nov 20, 2026) B2
public static class Icd10UsToWho
{
    public sealed record Entry(string UsCode, string WhoCode, string UsDescription, string WhoDescription);

    public static readonly IReadOnlyList<Entry> Entries =
    [
        new("J45.909", "J45.9", "Unspecified asthma, uncomplicated", "Asthma, unspecified"),
        new("Z00.129", "Z00.1", "Encounter for routine child health examination without abnormal findings", "Routine child health examination"),
        new("O24.419", "O24.4", "Gestational diabetes mellitus in pregnancy, unspecified control", "Diabetes mellitus arising in pregnancy"),
        new("O99.210", "O99.2", "Obesity complicating pregnancy, unspecified trimester", "Endocrine, nutritional and metabolic diseases complicating pregnancy, childbirth and the puerperium"),
        new("O14.90", "O14.9", "Unspecified pre-eclampsia, unspecified trimester", "Pre-eclampsia, unspecified"),
        new("O13.9", "O13", "Gestational hypertension without significant proteinuria, unspecified trimester", "Gestational [pregnancy-induced] hypertension"),
        new("A90", "A97.9", "Dengue fever [classical dengue]", "Dengue, unspecified"),
        new("A09", "A09.0", "Infectious gastroenteritis and colitis, unspecified", "Other and unspecified gastroenteritis and colitis of infectious origin"),
        new("O80", "O80.9", "Encounter for full-term uncomplicated delivery", "Single spontaneous delivery, unspecified"),
        new("O82", "O82.9", "Encounter for cesarean delivery without indication", "Delivery by caesarean section, unspecified"),
    ];

    public static readonly IReadOnlyDictionary<string, Entry> ByUsCode =
        Entries.ToDictionary(e => e.UsCode, StringComparer.OrdinalIgnoreCase);

    public static bool TryMapCode(string? code, out Entry entry)
    {
        entry = null!;
        var key = code?.Trim();
        if (string.IsNullOrEmpty(key)) return false;
        return ByUsCode.TryGetValue(key, out entry!);
    }

    public static string ReplaceCodesInText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var updated = text;
        foreach (var entry in Entries.OrderByDescending(e => e.UsCode.Length))
        {
            updated = TokenRegex(entry.UsCode).Replace(updated, entry.WhoCode);
        }
        return updated;
    }

    public static bool TryReplaceUsDescription(string? text, string whoDescription, string usDescription, out string? replaced)
    {
        replaced = text;
        if (text is null) return false;
        if (!string.Equals(text.Trim(), usDescription, StringComparison.OrdinalIgnoreCase)) return false;
        replaced = whoDescription;
        return true;
    }

    private static readonly Dictionary<string, Regex> TokenRegexes = new(StringComparer.OrdinalIgnoreCase);

    private static Regex TokenRegex(string usCode)
    {
        lock (TokenRegexes)
        {
            if (TokenRegexes.TryGetValue(usCode, out var rx)) return rx;
            rx = new Regex($@"(?<![A-Za-z0-9.]){Regex.Escape(usCode)}(?![A-Za-z0-9.])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            TokenRegexes[usCode] = rx;
            return rx;
        }
    }
}
//JJQR - END
