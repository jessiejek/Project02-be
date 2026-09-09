using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

public class Service : IHasUpdatedAt
{
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public ServiceCategory Category { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Composite PK: (doctor_id, service_id).</summary>
public class DoctorService
{
    public Guid DoctorId { get; set; }
    public Guid ServiceId { get; set; }
    public int DurationMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore] public Doctor Doctor { get; set; } = null!;
    [JsonPropertyName("services")] public Service Service { get; set; } = null!;
}
