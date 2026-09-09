using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

/// <summary>Upsert conflict used by UI: booking_id (unique).</summary>
public class Consultation : IHasUpdatedAt
{
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

public class ConsultationDiagnosis
{
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
public class PatientVitalReading : IHasUpdatedAt
{
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
public class FollowUp : IHasUpdatedAt
{
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

/// <summary>DDL exists; frontend does not query this table today.</summary>
public class Icd10Code
{
    public string Code { get; set; } = ""; // PK
    public string Description { get; set; } = "";
}

public class SoapPhrase : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public SoapField Field { get; set; }
    public string Label { get; set; } = "";
    public string Body { get; set; } = "";
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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
