using System.Text.Json.Serialization;

namespace ClinicApp.Domain.Entities;

//JJQR - AI (Oct 31, 2026)
public class PatientAllergy : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Allergen { get; set; } = "";
    public string? Reaction { get; set; }
    public string? Severity { get; set; }
    public bool IsNkda { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public Patient Patient { get; set; } = null!;
}
//JJQR - END
