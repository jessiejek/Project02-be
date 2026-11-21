using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicApp.Infrastructure;

//JJQR - AI (Nov 21, 2026) - the medicine autocomplete catalog, in EVERY environment (it used to hold 15 items from a migration plus 4 that existed only in the Development seeder).
/// <summary>Everyday primary-care medicines for adult and pediatric family medicine in the Philippines. Names are GENERIC names only (Generics Act, RA 6675) and every strength / form below
/// was checked against the DOH Philippine National Formulary Essential Medicines List (8th edition, as of Nov 2022). No controlled (dangerous-drug) medicines are included.
/// Idempotent: inserts what is missing (by id, and never a second row for a name already present) and corrects a few older names that did not match the formulary.
/// Dosing is NOT part of this catalog — the doctor writes the prescription.</summary>
public static class MedicineCatalogSeeder
{
    private static Guid Id(int n) => Guid.Parse($"22222222-0000-0000-0000-{n:D12}");

    /// <summary>Older rows whose names did not match the formulary: same id, corrected name.</summary>
    private static readonly (int N, string Name)[] Corrections =
    [
        (2,  "AMOXICILLIN + CLAVULANIC ACID (CO-AMOXICLAV) 500MG/125MG TAB"), // was "500MG TAB" — the clavulanate part was missing
        (3,  "CEFIXIME 200MG CAP"),                                            // the formulary lists capsules, not tablets
        (10, "ASCORBIC ACID 500MG TAB"),                                       // "ASCORBIC ACID + MULTIVITAMINS" is not a defined product
        (18, "FERROUS SALT + FOLIC ACID (60MG ELEMENTAL IRON + 400MCG) TAB"),  // the formulary combination has a stated strength
        (19, "CALCIUM CARBONATE (500MG ELEMENTAL CALCIUM) TAB"),               // the formulary gives elemental calcium, not the salt weight
    ];

    private static readonly (int N, string Name)[] Medicines =
    [
        (16, "NITROFURANTOIN 100MG CAP"),
        (17, "ORAL REHYDRATION SALTS (ORS) SACHET"),
        (18, "FERROUS SALT + FOLIC ACID (60MG ELEMENTAL IRON + 400MCG) TAB"),
        (19, "CALCIUM CARBONATE (500MG ELEMENTAL CALCIUM) TAB"),
        // antibiotics
        (20, "AMOXICILLIN 500MG CAP"),
        (21, "AMOXICILLIN 250MG/5ML SUSPENSION"),
        (22, "CEFALEXIN 500MG CAP"),
        (23, "CEFUROXIME 500MG TAB"),
        (24, "COTRIMOXAZOLE (SULFAMETHOXAZOLE + TRIMETHOPRIM) 800MG/160MG TAB"),
        (25, "CLARITHROMYCIN 500MG TAB"),
        (26, "METRONIDAZOLE 500MG TAB"),
        (27, "DOXYCYCLINE 100MG CAP"),
        (28, "CIPROFLOXACIN 500MG TAB"),
        // fever, pain (adult and child)
        (29, "PARACETAMOL 120MG/5ML SUSPENSION"),
        (30, "PARACETAMOL 250MG/5ML SUSPENSION"),
        (31, "IBUPROFEN 100MG/5ML SUSPENSION"),
        // allergy and breathing
        (32, "LORATADINE 10MG TAB"),
        (33, "MONTELUKAST 10MG TAB"),
        (34, "CETIRIZINE 5MG/5ML SYRUP"),
        (35, "SALBUTAMOL 200MCG/DOSE DRY POWDER INHALER"),
        // stomach
        (36, "ZINC (10MG ELEMENTAL) CHEWABLE TAB"),
        (37, "HYOSCINE-N-BUTYLBROMIDE 10MG TAB"),
        (38, "DOMPERIDONE 10MG TAB"),
        (39, "OMEPRAZOLE 40MG CAP"),
        // long-term conditions
        (40, "AMLODIPINE 10MG TAB"),
        (41, "LOSARTAN 100MG TAB"),
        (42, "GLICLAZIDE 80MG TAB"),
        (43, "ATORVASTATIN 20MG TAB"),
        // vitamins
        (44, "FOLIC ACID 5MG TAB"),
    ];

    public static async Task<int> SeedAsync(ClinicAppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var existing = await db.Medicines.ToListAsync(ct);
        var byId = existing.ToDictionary(m => m.MedicineId);
        var names = existing.Select(m => m.GenericName.Trim().ToUpperInvariant()).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var added = 0;
        var renamed = 0;

        foreach (var (n, name) in Corrections)
        {
            if (byId.TryGetValue(Id(n), out var row) && !string.Equals(row.GenericName, name, StringComparison.Ordinal))
            {
                names.Remove(row.GenericName.Trim().ToUpperInvariant());
                row.GenericName = name;
                names.Add(name.ToUpperInvariant());
                renamed++;
            }
        }

        foreach (var (n, name) in Medicines)
        {
            if (byId.ContainsKey(Id(n))) continue;                    // already there (a Development database has 16-19)
            if (!names.Add(name.ToUpperInvariant())) continue;         // never a second row for the same name
            db.Medicines.Add(new Medicine { MedicineId = Id(n), GenericName = name, CreatedAt = now });
            added++;
        }

        if (added + renamed > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation("MedicineCatalogSeeder: {Added} medicine(s) added, {Renamed} name(s) corrected.", added, renamed);
        return added + renamed;
    }

    /// <summary>The names this seeder guarantees (for tests).</summary>
    public static IEnumerable<string> GuaranteedNames => Medicines.Select(m => m.Name).Concat(Corrections.Select(c => c.Name));
}
//JJQR - END
