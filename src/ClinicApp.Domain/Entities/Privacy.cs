// JJQR - AI (Nov 20, 2026) - PV-12 / PV-14: consent history and patient privacy requests (RA 10173)
namespace ClinicApp.Domain.Entities;

public static class ConsentDecision
{
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Withdrawn = "Withdrawn";
    public static readonly string[] All = [Accepted, Declined, Withdrawn];
}

/// <summary>One dated, versioned consent decision. Rows are only ever added, never edited, so the history is the proof.</summary>
public class PatientConsent
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public int ConsentVersion { get; set; }
    /// <summary>Accepted | Declined | Withdrawn</summary>
    public string Decision { get; set; } = ConsentDecision.Accepted;
    public DateTimeOffset DecidedAt { get; set; }
}

public static class DataRequestKind
{
    public const string Correction = "Correction";
    public const string Deletion = "Deletion";
    public static readonly string[] All = [Correction, Deletion];
}

public static class DataRequestStatus
{
    public const string Pending = "Pending";
    public const string Scheduled = "Scheduled";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public static readonly string[] All = [Pending, Scheduled, Completed, Rejected];
}

/// <summary>A patient's request to correct or delete their data. Deleting medical records is not allowed
/// before the clinic's 7-year retention period ends, so a deletion is logged and <c>RetainUntil</c> says
/// when it can actually be carried out.</summary>
public class DataRequest
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>Correction | Deletion</summary>
    public string Kind { get; set; } = DataRequestKind.Correction;
    /// <summary>What the patient wants changed (their own words, kept short).</summary>
    public string Details { get; set; } = "";
    /// <summary>Pending | Scheduled | Completed | Rejected</summary>
    public string Status { get; set; } = DataRequestStatus.Pending;
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>Deletion only: the earliest date the medical record may be deleted (last visit + 7 years).</summary>
    public DateOnly? RetainUntil { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; }
}
// JJQR - END
