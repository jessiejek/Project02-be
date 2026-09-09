using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

// Keyless entities mapped to SQL views (Section 5 of frontend contract). Registered
// with .ToView(...).HasNoKey() in ClinicAppDbContext.

public class VDoctorRating
{
    public Guid? DoctorId { get; set; }
    public decimal? AverageRating { get; set; }
    public int? ReviewCount { get; set; }
}

public class VDailyBookingSummary
{
    public DateOnly? AppointmentDate { get; set; }
    public int? TotalBookings { get; set; }
    public int? CompletedCount { get; set; }
    public int? PaidCount { get; set; }
    public int? UnpaidCount { get; set; }
    public int? NoShowCount { get; set; }
    public decimal? Revenue { get; set; }
}

public class VUnpaidCompletedVisit
{
    public Guid? BookingId { get; set; }
    public Guid? PatientId { get; set; }
    public string? PatientCode { get; set; }
    public string? PatientName { get; set; }
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateOnly? AppointmentDate { get; set; }
    public decimal? AmountDue { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
}

public class VPendingFollowUp
{
    public Guid? FollowUpId { get; set; }
    public Guid? PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateOnly? FollowUpDate { get; set; }
    public string? Reason { get; set; }
    public FollowUpStatus? Status { get; set; }
}
