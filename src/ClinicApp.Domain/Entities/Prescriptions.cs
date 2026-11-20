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

    [JsonPropertyName("prescription_line_items")]
    public ICollection<PrescriptionLineItem> LineItems { get; set; } = new List<PrescriptionLineItem>();

    /// <summary>Nested embed: prescription_groups(bookings(appointment_date, doctors(staff_accounts(full_name)))).</summary>
    [JsonPropertyName("bookings")] public Booking? Booking { get; set; }
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
    // §16.8 Form 1 — structured Rx-pad fields (all optional; free-text `Instruction` stays the fallback).
    public string? Timing { get; set; }           // e.g. "1 tab TID"
    public string? MealRelation { get; set; }     // "before meals" | "after meals" | "with food" | ...
    public string? DurationKind { get; set; }     // "days" | "weeks" | "months" | "ongoing"
    public int? DurationValue { get; set; }
    public string? Indication { get; set; }       // "for fever", "for cough", ...
    //JJQR - AI (Oct 31, 2026)
    public string? Route { get; set; }
    //JJQR - END
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
    //JJQR - AI (Nov 19, 2026) - templates revamp: doctor-chosen order (0 = never reordered; ties fall back to name)
    public int SortOrder { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }
}

public class PrescriptionTemplate : IHasUpdatedAt
{
    public Guid TemplateId { get; set; }
    public Guid DoctorId { get; set; }
    public string Title { get; set; } = "";
    public bool IsSystemTemplate { get; set; }
    //JJQR - AI (Nov 20, 2026) A2
    public string? SourceBuiltinKey { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("prescription_template_items")]
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
    //JJQR - AI (Nov 19, 2026) - templates revamp: structured Rx-pad fields, same names/meaning as PrescriptionLineItem (all optional)
    public string? Timing { get; set; }
    public string? MealRelation { get; set; }
    public string? DurationKind { get; set; }
    public int? DurationValue { get; set; }
    public string? Indication { get; set; }
    public string? Route { get; set; }
    //JJQR - END
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore] public PrescriptionTemplate Template { get; set; } = null!;
}
