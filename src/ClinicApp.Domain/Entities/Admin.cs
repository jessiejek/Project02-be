using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }
    public AuditEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Action { get; set; } = "";
    public Guid? PerformedByUserId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
}

public class Announcement : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsActive { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Singleton row, Id = 1.</summary>
public class ClinicSetting
{
    public short Id { get; set; } = 1;
    public string ClinicName { get; set; } = "";
    public string Address { get; set; } = "";
    public string? ContactNumber { get; set; }
    public string? Email { get; set; }
    public string? Description { get; set; }
    public PaymentMode DefaultPaymentMode { get; set; }
    public string? RefundPolicy { get; set; }
    public int ConsentVersion { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? PrivacyPolicyText { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>PK: day_of_week (0-6).</summary>
public class ClinicOperatingHour
{
    public short DayOfWeek { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
}

/// <summary>PK: payment_method itself (no surrogate id).</summary>
public class ClinicAcceptedPaymentMethod
{
    public PaymentMethod PaymentMethod { get; set; }
}
