using ClinicApp.Domain.Entities;

namespace ClinicApp.Domain;

//JJQR - AI (Nov 21, 2026) - priority lane: queue numbers are R-001 (regular) / S-001 (senior or PWD); S always sorts ahead of R.
/// <summary>The two queue lanes. A queue number is "{lane}-{sequence}", e.g. <c>R-014</c> or <c>S-003</c>.
/// Each lane counts from 1 every day. Senior citizens and PWD are served ahead of the regular lane
/// (RA 9994 / RA 7277 priority lane); within a lane it is first-come-first-served.
/// Pre-lane numbers ("Q-052") are treated as regular.</summary>
public static class QueueLane
{
    public const string Regular = "R";
    public const string Priority = "S";
    /// <summary>Constant so EF can translate <c>StartsWith</c> into a SQL LIKE.</summary>
    public const string PriorityPrefix = "S-";

    /// <summary>Senior citizens are 60 and over (RA 9994).</summary>
    public const int SeniorAge = 60;

    /// <summary>The only discount categories: "Senior" or "PWD" (any casing). Returns the canonical value, or null for blank / anything else.</summary>
    public static string? NormalizeDiscount(string? raw)
    {
        var t = raw?.Trim();
        if (string.Equals(t, "Senior", StringComparison.OrdinalIgnoreCase)) return "Senior";
        if (string.Equals(t, "PWD", StringComparison.OrdinalIgnoreCase)) return "PWD";
        return null;
    }

    /// <summary>True for blank (no discount) or a real category; false for any other text, which must be refused.</summary>
    public static bool IsValidDiscount(string? raw) => string.IsNullOrWhiteSpace(raw) || NormalizeDiscount(raw) is not null;

    public static bool IsPriority(string? queueNumber) =>
        queueNumber is not null && queueNumber.StartsWith(PriorityPrefix, StringComparison.Ordinal);

    public static string Format(string lane, int sequence) => $"{lane}-{sequence:D3}";

    /// <summary>Which lane a patient queues in on <paramref name="onDate"/>.
    /// Priority when ANY of: the desk picked a Senior/PWD discount, the patient is 60+ that day,
    /// or a Senior / PWD ID number is on file. Otherwise regular.</summary>
    public static string For(Patient? patient, string? discountCategory, DateOnly onDate)
    {
        if (NormalizeDiscount(discountCategory) is not null) return Priority; //JJQR - AI (Nov 21, 2026) - only a real Senior / PWD category, not any text
        if (patient is null) return Regular;
        if (!string.IsNullOrWhiteSpace(patient.SeniorIdNumber) || !string.IsNullOrWhiteSpace(patient.PwdIdNumber)) return Priority;
        return IsSenior(patient.DateOfBirth, onDate) ? Priority : Regular;
    }

    public static bool IsSenior(DateOnly dateOfBirth, DateOnly onDate)
    {
        if (dateOfBirth.Year < 1900) return false; // placeholder / unset birth date, not a 125-year-old
        var age = onDate.Year - dateOfBirth.Year;
        if (onDate < dateOfBirth.AddYears(age)) age--;
        return age >= SeniorAge;
    }
}
//JJQR - END
