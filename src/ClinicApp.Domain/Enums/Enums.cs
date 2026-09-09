namespace ClinicApp.Domain.Enums;

// All 18 enums per DOTNET_FRONTEND_CONTRACT.md §3.
// Member names are the EXACT wire strings (JsonStringEnumConverter serializes by member name).
// Do not rename members — the frontend matches these character-for-character.

public enum UserRole
{
    Patient,
    Staff,
    Doctor,
    Admin
}

public enum StaffRole
{
    Staff,
    Doctor,
    Admin
}

public enum StaffStatus
{
    Active,
    Inactive,
    Invited,
    OnLeave
}

public enum SexType
{
    Male,
    Female
}

public enum DoctorDayStatusEnum
{
    Available,
    RunningLate,
    UnavailableToday
}

public enum ServiceCategory
{
    Consultation,
    Procedure,
    Laboratory,
    Diagnostic
}

/// <summary>§16.6 — doctor-set at consultation; drives which fee applies.</summary>
public enum VisitType
{
    New,
    FollowUp
}

public enum BookingStatus
{
    Pending,
    ProofSubmitted,
    Confirmed,
    CheckedIn,
    InProgress,
    OnHold,
    Cancelled,
    Completed,
    Expired,
    NoShow,
    Rescheduled
}

public enum PaymentMode
{
    Online,
    PayAtClinic
}

public enum PaymentStatus
{
    Unpaid,
    Paid,
    Waived,
    Refunded
}

public enum PaymentMethod
{
    Cash,
    GCash,
    Maya,
    BankTransfer
}

public enum ProofType
{
    ReferenceNumber,
    Screenshot
}

public enum ConsultationStatus
{
    Draft,
    Completed,
    Amended
}

public enum DiagnosisType
{
    Primary,
    Secondary,
    Differential,
    Comorbidity
}

public enum SoapField
{
    ChiefComplaint,
    Subjective,
    Objective,
    Assessment,
    Plan
}

public enum LabOrderStatus
{
    Requested,
    Completed
}

public enum VaccinationStatus
{
    Administered,
    Scheduled,
    Overdue
}

public enum VaccinationSource
{
    AdministeredInClinic,
    PatientReported,
    ExternalRecord
}

public enum AuditEntityType
{
    Booking,
    Patient,
    Doctor,
    Payment,
    Settings,
    Consultation,
    Staff
}

public enum FollowUpStatus
{
    Pending,
    Completed
}
