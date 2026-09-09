using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

/// <summary>§16.8 Form 3 — fixed catalog of the clinic's pre-printed lab panel
/// (same shape as `medicines` / `vital_field_templates`). A checked box ⇒ an
/// order row with `LabTestId` set; a handwritten test ⇒ `LabTestId` null +
/// free-text `TestName`.</summary>
public class LabTestCatalog
{
    public Guid LabTestId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }   // true for the 9 pre-printed rows
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>DDL exists; consultation UI currently keeps labs local-only — build the endpoint anyway.</summary>
public class LabOrder : IHasUpdatedAt
{
    public Guid LabOrderId { get; set; }
    public Guid ConsultationId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    /// <summary>§16.8 — set when the order came from a catalog checkbox; null for a handwritten test.</summary>
    public Guid? LabTestId { get; set; }
    public string TestName { get; set; } = "";
    public string? TestCode { get; set; }
    public string? Reason { get; set; }
    public string? ClinicalIndication { get; set; }
    public string? SpecimenType { get; set; }
    public string? Notes { get; set; }
    public LabOrderStatus Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? ResultAttachmentUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Reads exist; consultation write path is still local-only in FE — endpoint kept for future use.</summary>
public class PatientVaccination : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? ConsultationId { get; set; }
    public string VaccineName { get; set; } = "";
    public string? Manufacturer { get; set; }
    public short? DoseNumber { get; set; }
    public string? Route { get; set; }
    public string? Site { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateOnly? AdministeredDate { get; set; }
    public Guid? AdministeredBy { get; set; }
    public DateOnly? NextDoseDate { get; set; }
    public VaccinationStatus Status { get; set; }
    public VaccinationSource Source { get; set; }
    public string? Notes { get; set; }
    public string? ReactionNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class PatientDocument
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? ConsultationId { get; set; }
    public string FileName { get; set; } = "";
    public long? FileSize { get; set; }
    public string? FileContentType { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string FileUrl { get; set; } = "";
    public Guid? UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    [JsonPropertyName("bookings")] public Booking? Booking { get; set; }
}

public class PatientLabResult
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? ConsultationId { get; set; }
    public Guid? LabOrderId { get; set; }
    public string FileName { get; set; } = "";
    public string? FileContentType { get; set; }
    public string? ResultTitle { get; set; }
    public string? ResultText { get; set; }
    /// <summary>Plain text, not an enum — default "Completed" per contract.</summary>
    public string Status { get; set; } = "Completed";
    public string FileUrl { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }

    [JsonPropertyName("bookings")] public Booking? Booking { get; set; }
}
