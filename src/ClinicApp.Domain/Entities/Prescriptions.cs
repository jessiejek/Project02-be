using System.Text.Json.Serialization;

namespace ClinicApp.Domain.Entities;

public class Medicine
{
    public Guid MedicineId { get; set; }
    public string GenericName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class PrescriptionGroup : IHasUpdatedAt
{
    public Guid GroupId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid BookingId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PrescriptionLineItem> LineItems { get; set; } = new List<PrescriptionLineItem>();
}

public class PrescriptionLineItem
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid MedicineId { get; set; }
    public string GenericName { get; set; } = "";
    public string Dosage { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string? Instruction { get; set; }
    public bool IsControlledSubstance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore] public PrescriptionGroup Group { get; set; } = null!;
}

public class DoctorFavoriteMedicine
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public Guid MedicineId { get; set; }
    public string GenericName { get; set; } = "";
    public string Dosage { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string? Instruction { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PrescriptionTemplate : IHasUpdatedAt
{
    public Guid TemplateId { get; set; }
    public Guid DoctorId { get; set; }
    public string Title { get; set; } = "";
    public bool IsSystemTemplate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<PrescriptionTemplateItem> Items { get; set; } = new List<PrescriptionTemplateItem>();
}

public class PrescriptionTemplateItem
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public Guid MedicineId { get; set; }
    public string GenericName { get; set; } = "";
    public string Dosage { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string? Instruction { get; set; }
    public bool IsControlledSubstance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore] public PrescriptionTemplate Template { get; set; } = null!;
}
