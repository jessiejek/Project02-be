using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

/// <summary>Upsert conflict used by UI: booking_id (unique).</summary>
//JJQR - AI (Nov 21, 2026) cloud sync: class now implements ISyncTracked
public class Consultation : IHasUpdatedAt, ISyncTracked
//JJQR - END
{
    //JJQR - AI (Nov 21, 2026) cloud sync columns (ISyncTracked)
    public long RowVersion { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? OriginNodeId { get; set; }
    //JJQR - END
    public Guid ConsultationId { get; set; }
    public Guid BookingId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public ConsultationStatus Status { get; set; }
    public string? ChiefComplaint { get; set; }
    public string? Subjective { get; set; }
    public string? Objective { get; set; }
    public string? Assessment { get; set; }
    public string? Plan { get; set; }
    public string? DoctorNotes { get; set; }

    // §16.6 Professional Fee decision — the doctor's explicit call on the PF for
    // this visit. 'Charge' | 'Waive' | null (not yet decided). Recorded for the
    // clinical/billing record; the flat fee itself is still computed from the
    // booking's visit_type / med_cert / discount.
    public string? PfDecision { get; set; }
    public decimal? PfAmount { get; set; }
    public string? PfWaiveReason { get; set; }

    public Guid? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Nested embeds per contract §6 — populated only on reads that .Include() them.
    [JsonPropertyName("bookings")] public Booking? Booking { get; set; }
    [JsonPropertyName("doctors")] public Doctor? Doctor { get; set; }
    [JsonPropertyName("consultation_diagnoses")] public ICollection<ConsultationDiagnosis> ConsultationDiagnoses { get; set; } = new List<ConsultationDiagnosis>();
    [JsonPropertyName("follow_ups")] public FollowUp? FollowUp { get; set; }
}

/// <summary>§16.8 Form 2. One per consultation (upsert conflict key = consultation_id).
/// Doctor license / PTR are read live from `doctors` at print time, not stored here.</summary>
//JJQR - AI (Nov 21, 2026) cloud sync: class now implements ISyncTracked
public class MedicalCertificate : IHasUpdatedAt, ISyncTracked
//JJQR - END
{
    //JJQR - AI (Nov 21, 2026) cloud sync columns (ISyncTracked)
    public long RowVersion { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? OriginNodeId { get; set; }
    //JJQR - END
    public Guid CertificateId { get; set; }
    public Guid ConsultationId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public DateOnly IssueDate { get; set; }
    public string? PatientAddressSnapshot { get; set; }  // "residing at ..."
    public string? ExaminedAt { get; set; }               // "has been examined in ..." (place)
    public DateOnly? ExaminationDateFrom { get; set; }     // "on ..."
    public DateOnly? ExaminationDateTo { get; set; }       // "until ..."
    public string? DiagnosisText { get; set; }
    public string? Recommendations { get; set; }
    public string? PurposeException { get; set; }          // the "except ______" blank
    public DateOnly? ComeBackOn { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

//JJQR - AI (Nov 21, 2026) cloud sync: class now implements ISyncTracked
public class ConsultationDiagnosis : ISyncTracked
//JJQR - END
{
    //JJQR - AI (Nov 21, 2026) cloud sync columns (ISyncTracked)
    public long RowVersion { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? OriginNodeId { get; set; }
    //JJQR - END
    public Guid Id { get; set; }
    public Guid ConsultationId { get; set; }
    public string? Icd10Code { get; set; }
    public string? CustomDescription { get; set; }
    public DiagnosisType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class VitalFieldTemplate
{
    public Guid TemplateId { get; set; }
    public string Description { get; set; } = "";
    public string FormKey { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Conflict: (booking_id, template_id).</summary>
//JJQR - AI (Nov 21, 2026) cloud sync: class now implements ISyncTracked
public class PatientVitalReading : IHasUpdatedAt, ISyncTracked
//JJQR - END
{
    //JJQR - AI (Nov 21, 2026) cloud sync columns (ISyncTracked)
    public long RowVersion { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? OriginNodeId { get; set; }
    //JJQR - END
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid PatientId { get; set; }
    public Guid TemplateId { get; set; }
    public string Value { get; set; } = "";
    public DateOnly RecordedAt { get; set; }
    /// <summary>§16.1 — staff-like user who took the reading at intake.</summary>
    public Guid? RecordedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Conflict: consultation_id (unique). View v_pending_follow_ups exposes Id as follow_up_id.</summary>
//JJQR - AI (Nov 21, 2026) cloud sync: class now implements ISyncTracked
public class FollowUp : IHasUpdatedAt, ISyncTracked
//JJQR - END
{
    //JJQR - AI (Nov 21, 2026) cloud sync columns (ISyncTracked)
    public long RowVersion { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? OriginNodeId { get; set; }
    //JJQR - END
    public Guid Id { get; set; }
    public Guid ConsultationId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public DateOnly FollowUpDate { get; set; }
    public string? Reason { get; set; }
    public string? Instructions { get; set; }
    public bool ReminderEnabled { get; set; }
    public FollowUpStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>WHO ICD-10 2019 catalog (PhilHealth). Loaded by Icd10Seeder, not HasData.</summary>
public class Icd10Code
{
    public string Code { get; set; } = ""; // PK
    public string Description { get; set; } = "";
    //JJQR - AI (Nov 20, 2026) B1
    public int Level { get; set; }
    public bool IsLeaf { get; set; } = true;
    //JJQR - END
}

//JJQR - AI (Oct 31, 2026)
/// <summary>Structured numeric lab results per patient (e.g. HBA1C, CREATININE).</summary>
public class PatientLabValue
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string TestCode { get; set; } = "";
    public decimal Value { get; set; }
    public string Unit { get; set; } = "";
    public DateOnly ObservedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
//JJQR - END

public class SoapPhrase : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public SoapField Field { get; set; }
    public string Label { get; set; } = "";
    public string Body { get; set; } = "";
    //JJQR - AI (Nov 19, 2026) - templates revamp: doctor-chosen order within a field (0 = never reordered; ties fall back to label)
    public int SortOrder { get; set; }
    //JJQR - END
    //JJQR - AI (Nov 20, 2026) A2
    public string? SourceBuiltinKey { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class SoapTemplate : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public string Title { get; set; } = "";
    public bool IsSystemTemplate { get; set; }
    public string? ChiefComplaint { get; set; }
    public string? Subjective { get; set; }
    public string? Objective { get; set; }
    public string? Assessment { get; set; }
    public string? Plan { get; set; }
    //JJQR - AI (Nov 20, 2026) A2
    //JJQR - AI (Nov 21, 2026) - which patients this template is for: "Adult" or "Pedia" (family medicine). Null = not chosen yet (the app falls back to the title).
    public string? Category { get; set; }
    //JJQR - END
    public string? SourceBuiltinKey { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>medical_certificate_templates — canned "reason for the cert" text
/// (e.g. "Fit to Work", "Sick Leave 3 days") a doctor reuses across §16.8
/// Form 2 issuances. Only the free-text fields that are actually reused;
/// dates and the patient's address stay per-issuance.</summary>
public class MedicalCertificateTemplate : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public string Title { get; set; } = "";
    public bool IsSystemTemplate { get; set; }
    public string? DiagnosisText { get; set; }
    public string? Recommendations { get; set; }
    public string? PurposeException { get; set; }
    //JJQR - AI (Nov 20, 2026) A2
    public string? SourceBuiltinKey { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>doctor_diagnosis_templates — a doctor's reusable free-text diagnoses
/// (§16.8). Picked into the consultation Diagnosis section; managed on
/// /doctor/templates. Plain text, no ICD coding.</summary>
public class DoctorDiagnosisTemplate : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public string Label { get; set; } = "";
    public string Body { get; set; } = "";
    //JJQR - AI (Nov 19, 2026) - templates revamp: optional ICD-10 code (no FK; icd10_codes.code is a string key)
    public string? Icd10Code { get; set; }
    //JJQR - END
    //JJQR - AI (Nov 20, 2026) A2
    public string? SourceBuiltinKey { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

//JJQR - AI (Nov 20, 2026) A2 — one row per (doctor, kind, builtin) once a built-in has been copied or adopted; kept after she deletes the copy so GET does not bring it back.
public class DoctorBuiltinTemplateCopy
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public string Kind { get; set; } = "";
    public string BuiltinKey { get; set; } = "";
    public DateTimeOffset CopiedAt { get; set; }
}
//JJQR - END
