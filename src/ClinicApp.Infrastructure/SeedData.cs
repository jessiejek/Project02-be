using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

/// <summary>Verbatim port of the seed section at the bottom of supabase/schema.sql
/// (AAA MD AAAA/alyn/supabase/schema.sql, lines 895-958) — see DOTNET_BACKEND_PLAN.md §5.8.
/// Fixed GUIDs so HasData produces a stable migration.</summary>
internal static class SeedData
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClinicSetting>().HasData(new ClinicSetting
        {
            Id = 1,
            ClinicName = "Dr. Grace Gavino Medical Clinic",
            Address = "TBD",
            DefaultPaymentMode = PaymentMode.PayAtClinic,
            ConsentVersion = 1,
            UpdatedAt = SeedTimestamp
        });

        modelBuilder.Entity<ClinicOperatingHour>().HasData(
            new ClinicOperatingHour { DayOfWeek = 0, IsClosed = true, OpenTime = null, CloseTime = null },
            new ClinicOperatingHour { DayOfWeek = 1, IsClosed = false, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(17, 0) },
            new ClinicOperatingHour { DayOfWeek = 2, IsClosed = false, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(17, 0) },
            new ClinicOperatingHour { DayOfWeek = 3, IsClosed = false, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(17, 0) },
            new ClinicOperatingHour { DayOfWeek = 4, IsClosed = false, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(17, 0) },
            new ClinicOperatingHour { DayOfWeek = 5, IsClosed = false, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(17, 0) },
            new ClinicOperatingHour { DayOfWeek = 6, IsClosed = false, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(12, 0) }
        );

        modelBuilder.Entity<ClinicAcceptedPaymentMethod>().HasData(
            new ClinicAcceptedPaymentMethod { PaymentMethod = PaymentMethod.Cash },
            new ClinicAcceptedPaymentMethod { PaymentMethod = PaymentMethod.GCash },
            new ClinicAcceptedPaymentMethod { PaymentMethod = PaymentMethod.Maya },
            new ClinicAcceptedPaymentMethod { PaymentMethod = PaymentMethod.BankTransfer }
        );

        modelBuilder.Entity<Icd10Code>().HasData(
            new Icd10Code { Code = "Z00.0", Description = "General health examination" },
            new Icd10Code { Code = "I10", Description = "Essential (primary) hypertension" },
            new Icd10Code { Code = "J06.9", Description = "Acute upper respiratory infection, unspecified" },
            new Icd10Code { Code = "E11.9", Description = "Type 2 diabetes mellitus without complications" },
            new Icd10Code { Code = "J45.909", Description = "Unspecified asthma, uncomplicated" }
        );

        modelBuilder.Entity<VitalFieldTemplate>().HasData(
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000001"), Description = "TEMPERATURE", FormKey = "temperature", Unit = "°C", Icon = "thermometer", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000002"), Description = "PULSE RATE", FormKey = "pulse_rate", Unit = "bpm", Icon = "heart_pulse", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000003"), Description = "RESPIRATORY RATE", FormKey = "respiratory_rate", Unit = "rpm", Icon = "lungs", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000004"), Description = "BLOOD PRESSURE", FormKey = "blood_pressure", Unit = "mmHg", Icon = "gauge", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000005"), Description = "O2 SATURATION", FormKey = "o2_saturation", Unit = "%", Icon = "wind", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000006"), Description = "HEIGHT", FormKey = "height", Unit = "cm", Icon = "ruler", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000007"), Description = "WEIGHT", FormKey = "weight", Unit = "kg", Icon = "weight_scale", IsDefault = true, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000008"), Description = "Fundal Height", FormKey = "fundal_height", Unit = "", Icon = "ruler", IsDefault = false, CreatedAt = SeedTimestamp },
            new VitalFieldTemplate { TemplateId = Guid.Parse("11111111-0000-0000-0000-000000000009"), Description = "Fetal Heart Rate", FormKey = "fetal_heart_rate", Unit = "", Icon = "heart_pulse", IsDefault = false, CreatedAt = SeedTimestamp }
        );

        modelBuilder.Entity<Medicine>().HasData(
            Med(1, "PARACETAMOL 500MG TAB"),
            Med(2, "AMOXICILLIN + CLAVULANIC ACID (CO-AMOXICLAV) 500MG TAB"),
            Med(3, "CEFIXIME 200MG TAB"),
            Med(4, "LOPERAMIDE 2MG CAP"),
            Med(5, "MEFENAMIC ACID 500MG TAB"),
            Med(6, "CETIRIZINE 10MG TAB"),
            Med(7, "OMEPRAZOLE 20MG CAP"),
            Med(8, "METFORMIN 500MG TAB"),
            Med(9, "LOSARTAN 50MG TAB"),
            Med(10, "ASCORBIC ACID + MULTIVITAMINS 500MG TAB"),
            Med(11, "SALBUTAMOL 2MG/5ML SYRUP"),
            Med(12, "AMLODIPINE 5MG TAB"),
            Med(13, "AZITHROMYCIN 500MG TAB"),
            Med(14, "IBUPROFEN 400MG TAB"),
            Med(15, "SIMVASTATIN 20MG TAB")
        );
    }

    private static readonly DateTimeOffset SeedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Medicine Med(int n, string genericName) => new()
    {
        MedicineId = Guid.Parse($"22222222-0000-0000-0000-{n:D12}"),
        GenericName = genericName,
        CreatedAt = SeedTimestamp
    };
}
