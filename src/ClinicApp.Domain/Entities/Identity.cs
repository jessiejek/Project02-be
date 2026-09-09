using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

/// <summary>profiles — id is FK to ClinicApp.Auth.User.Id (formerly auth.users.id).</summary>
public class Profile
{
    public Guid Id { get; set; }
    public UserRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Patient : IHasUpdatedAt
{
    public Guid PatientId { get; set; }
    public Guid? UserId { get; set; }
    public string PatientCode { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public SexType Sex { get; set; }
    public string? CivilStatus { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
    public string? ContactNumber { get; set; }
    public string Email { get; set; } = "";
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactNumber { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? BloodType { get; set; }
    public string? PhilhealthNumber { get; set; }
    public string? HmoProvider { get; set; }
    public string? HmoCardNumber { get; set; }
    public bool IsGuest { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTimeOffset? ConsentedAt { get; set; }
    public int ConsentVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class StaffAccount : IHasUpdatedAt
{
    public Guid StaffId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? ContactNumber { get; set; }
    public StaffRole Role { get; set; }
    public StaffStatus Status { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset InvitedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public Doctor? Doctor { get; set; }
}

/// <summary>doctors — DoctorId = StaffAccounts.StaffId (1:1, shared PK).</summary>
public class Doctor : IHasUpdatedAt
{
    public Guid DoctorId { get; set; }
    public string Specialization { get; set; } = "";
    public decimal ConsultationFee { get; set; }
    public string? Bio { get; set; }
    public string? LicenseNumber { get; set; }
    public string? PtrNumber { get; set; }
    // Naming convention yields "s2number" (no underscore before a digit); contract §4 wants "s2_number".
    [Column("s2_number")]
    public string? S2Number { get; set; }
    public int SlotDurationMinutes { get; set; }
    public int SlotCapacity { get; set; }
    public int? DailyPatientLimit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Nested embed key per contract §6: doctors(staff_accounts(...)).</summary>
    [JsonPropertyName("staff_accounts")]
    public StaffAccount StaffAccount { get; set; } = null!;
}
