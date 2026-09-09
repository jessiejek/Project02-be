namespace ClinicApp.Domain.Entities;

/// <summary>Entities implementing this get UpdatedAt auto-stamped in ClinicAppDbContext.SaveChangesAsync.</summary>
public interface IHasUpdatedAt
{
    DateTimeOffset UpdatedAt { get; set; }
}
