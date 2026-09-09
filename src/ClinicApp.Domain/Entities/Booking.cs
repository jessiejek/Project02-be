using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

public class Booking : IHasUpdatedAt
{
    public Guid BookingId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly SlotStartTime { get; set; }
    public TimeOnly SlotEndTime { get; set; }
    public BookingStatus Status { get; set; }
    public PaymentMode PaymentMode { get; set; }
    public string? QueueNumber { get; set; }
    public decimal ConsultationFeeSnapshot { get; set; }
    public decimal TotalFee { get; set; }
    public decimal AmountDue { get; set; }
    public bool IsWalkIn { get; set; }

    // §16.6 — doctor sets VisitType at consultation; fee recomputed on complete.
    public VisitType VisitType { get; set; } = VisitType.New;
    /// <summary>'Senior' | 'PWD' | null — snapshot of the discount line applied.</summary>
    public string? DiscountCategory { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>Patient asked for a medical certificate → +FeeMedCert on the total.</summary>
    public bool MedCertRequested { get; set; }

    public ProofType? ProofType { get; set; }
    public string? ProofValue { get; set; }
    public DateTimeOffset? ProofSubmittedAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Nested embed keys per contract §6, e.g. bookings(doctors(staff_accounts(full_name))),
    // patients(first_name, last_name, ...), payments(status).
    [JsonPropertyName("patients")] public Patient Patient { get; set; } = null!;
    [JsonPropertyName("doctors")] public Doctor Doctor { get; set; } = null!;
    [JsonPropertyName("booking_services")] public ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();
    [JsonPropertyName("payments")] public Payment? Payment { get; set; }
}

/// <summary>Composite PK: (booking_id, service_id).</summary>
public class BookingService
{
    public Guid BookingId { get; set; }
    public Guid ServiceId { get; set; }
    public decimal PriceAtBooking { get; set; }

    [JsonIgnore] public Booking Booking { get; set; } = null!;
    [JsonPropertyName("services")] public Service Service { get; set; } = null!;
}

/// <summary>1:1 with Booking.</summary>
public class Payment : IHasUpdatedAt
{
    public Guid PaymentId { get; set; }
    public Guid BookingId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? OrNumber { get; set; }
    public decimal? AmountReceived { get; set; }
    public string? ConfirmNotes { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? WaivedByUserId { get; set; }
    public string? WaivedReason { get; set; }
    public DateTimeOffset? WaivedAt { get; set; }
    public Guid? RefundedByUserId { get; set; }
    public decimal? RefundAmount { get; set; }
    public string? RefundReason { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public Booking Booking { get; set; } = null!;
}

public class Review
{
    public Guid ReviewId { get; set; }
    public Guid BookingId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid PatientId { get; set; }
    public short Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
