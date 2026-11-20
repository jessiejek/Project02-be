using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Copies Admin-managed system SOAP/Rx/Cert rows (and catalog diagnoses/phrases) into a doctor's
/// own rows, once. Tracking in <see cref="DoctorBuiltinTemplateCopy"/> makes GET idempotent: a
/// deleted copy stays gone until restore.
/// </summary>
//JJQR - AI (Nov 20, 2026) A2 / A4
public sealed class BuiltInTemplateCopier(ClinicAppDbContext db)
{
    public async Task EnsureCopiesAsync(Guid doctorId, string kind, CancellationToken ct = default)
    {
        var k = BuiltInTemplateKind.Normalize(kind);
        if (!BuiltInTemplateKind.IsCopyable(k)) return;

        var recorded = await RecordedKeysAsync(doctorId, k!, ct);
        foreach (var src in await LoadSourcesAsync(k!, ct))
        {
            if (recorded.Contains(src.Key)) continue;
            await AdoptOrCopyAsync(doctorId, k!, src, restoreMissing: false, ct);
            recorded.Add(src.Key);
        }
    }

    /// <summary>Re-adds built-ins she deleted. Never overwrites an existing row. Returns how many copies were inserted.</summary>
    public async Task<int> RestoreAsync(Guid doctorId, string kind, CancellationToken ct = default)
    {
        var k = BuiltInTemplateKind.Normalize(kind);
        if (!BuiltInTemplateKind.IsCopyable(k)) return 0;

        var restored = 0;
        foreach (var src in await LoadSourcesAsync(k!, ct))
        {
            if (await AdoptOrCopyAsync(doctorId, k!, src, restoreMissing: true, ct))
                restored++;
        }
        return restored;
    }

    private async Task<HashSet<string>> RecordedKeysAsync(Guid doctorId, string kind, CancellationToken ct) =>
        (await db.DoctorBuiltinTemplateCopies.AsNoTracking()
            .Where(c => c.DoctorId == doctorId && c.Kind == kind)
            .Select(c => c.BuiltinKey)
            .ToListAsync(ct))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<BuiltinSource>> LoadSourcesAsync(string kind, CancellationToken ct) => kind switch
    {
        BuiltInTemplateKind.Soap => (await db.SoapTemplates.AsNoTracking()
            .Where(t => t.IsSystemTemplate).ToListAsync(ct))
            .Select(t => BuiltinSource.FromSoap(t)).ToList(),
        BuiltInTemplateKind.Rx => (await db.PrescriptionTemplates.AsNoTracking().Include(t => t.Items)
            .Where(t => t.IsSystemTemplate).ToListAsync(ct))
            .Select(t => BuiltinSource.FromRx(t)).ToList(),
        BuiltInTemplateKind.Cert => (await db.MedicalCertificateTemplates.AsNoTracking()
            .Where(t => t.IsSystemTemplate).ToListAsync(ct))
            .Select(t => BuiltinSource.FromCert(t)).ToList(),
        BuiltInTemplateKind.Diagnosis => BuiltInTemplateCatalog.Diagnoses
            .Select(d => BuiltinSource.FromDiagnosis(d)).ToList(),
        BuiltInTemplateKind.Phrase => BuiltInTemplateCatalog.Phrases
            .Select(p => BuiltinSource.FromPhrase(p)).ToList(),
        _ => [],
    };

    /// <returns>True when a new owned row was inserted (restore counts this).</returns>
    private async Task<bool> AdoptOrCopyAsync(Guid doctorId, string kind, BuiltinSource src, bool restoreMissing, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await FindOwnMatchAsync(doctorId, kind, src, ct);

        if (existing is MatchKind.BySourceKey or MatchKind.ByTitle)
        {
            await TrySaveAsync(ct); // persist a newly stamped source_builtin_key
            await EnsureTrackingAsync(doctorId, kind, src.Key, now, ct);
            return false;
        }

        if (!restoreMissing)
        {
            var alreadyRecorded = await db.DoctorBuiltinTemplateCopies.AsNoTracking()
                .AnyAsync(c => c.DoctorId == doctorId && c.Kind == kind && c.BuiltinKey == src.Key, ct);
            if (alreadyRecorded) return false;
        }

        InsertCopy(doctorId, kind, src, now);
        await TrySaveAsync(ct);
        var inserted = await HasOwnBySourceKeyAsync(doctorId, kind, src.Key, ct);
        await EnsureTrackingAsync(doctorId, kind, src.Key, now, ct);
        return inserted;
    }

    private async Task<bool> HasOwnBySourceKeyAsync(Guid doctorId, string kind, string key, CancellationToken ct) => kind switch
    {
        BuiltInTemplateKind.Soap => await db.SoapTemplates.AsNoTracking()
            .AnyAsync(t => t.DoctorId == doctorId && !t.IsSystemTemplate && t.SourceBuiltinKey == key, ct),
        BuiltInTemplateKind.Rx => await db.PrescriptionTemplates.AsNoTracking()
            .AnyAsync(t => t.DoctorId == doctorId && !t.IsSystemTemplate && t.SourceBuiltinKey == key, ct),
        BuiltInTemplateKind.Cert => await db.MedicalCertificateTemplates.AsNoTracking()
            .AnyAsync(t => t.DoctorId == doctorId && !t.IsSystemTemplate && t.SourceBuiltinKey == key, ct),
        BuiltInTemplateKind.Diagnosis => await db.DoctorDiagnosisTemplates.AsNoTracking()
            .AnyAsync(t => t.DoctorId == doctorId && t.SourceBuiltinKey == key, ct),
        BuiltInTemplateKind.Phrase => await db.SoapPhrases.AsNoTracking()
            .AnyAsync(t => t.DoctorId == doctorId && t.SourceBuiltinKey == key, ct),
        _ => false,
    };

    private enum MatchKind { None, BySourceKey, ByTitle }

    private async Task<MatchKind> FindOwnMatchAsync(Guid doctorId, string kind, BuiltinSource src, CancellationToken ct)
    {
        static bool Same(string? a, string b) =>
            string.Equals((a ?? "").Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        switch (kind)
        {
            case BuiltInTemplateKind.Soap:
            {
                var mine = await db.SoapTemplates.Where(t => t.DoctorId == doctorId && !t.IsSystemTemplate).ToListAsync(ct);
                if (mine.Any(t => t.SourceBuiltinKey == src.Key)) return MatchKind.BySourceKey;
                var hit = mine.FirstOrDefault(t => Same(t.Title, src.Title));
                if (hit is null) return MatchKind.None;
                hit.SourceBuiltinKey ??= src.Key;
                return MatchKind.ByTitle;
            }
            case BuiltInTemplateKind.Rx:
            {
                var mine = await db.PrescriptionTemplates.Where(t => t.DoctorId == doctorId && !t.IsSystemTemplate).ToListAsync(ct);
                if (mine.Any(t => t.SourceBuiltinKey == src.Key)) return MatchKind.BySourceKey;
                var hit = mine.FirstOrDefault(t => Same(t.Title, src.Title));
                if (hit is null) return MatchKind.None;
                hit.SourceBuiltinKey ??= src.Key;
                return MatchKind.ByTitle;
            }
            case BuiltInTemplateKind.Cert:
            {
                var mine = await db.MedicalCertificateTemplates.Where(t => t.DoctorId == doctorId && !t.IsSystemTemplate).ToListAsync(ct);
                if (mine.Any(t => t.SourceBuiltinKey == src.Key)) return MatchKind.BySourceKey;
                var hit = mine.FirstOrDefault(t => Same(t.Title, src.Title));
                if (hit is null) return MatchKind.None;
                hit.SourceBuiltinKey ??= src.Key;
                return MatchKind.ByTitle;
            }
            case BuiltInTemplateKind.Diagnosis:
            {
                var mine = await db.DoctorDiagnosisTemplates.Where(t => t.DoctorId == doctorId).ToListAsync(ct);
                if (mine.Any(t => t.SourceBuiltinKey == src.Key)) return MatchKind.BySourceKey;
                var hit = mine.FirstOrDefault(t => Same(t.Label, src.Title));
                if (hit is null) return MatchKind.None;
                hit.SourceBuiltinKey ??= src.Key;
                return MatchKind.ByTitle;
            }
            case BuiltInTemplateKind.Phrase:
            {
                var mine = await db.SoapPhrases.Where(t => t.DoctorId == doctorId).ToListAsync(ct);
                if (mine.Any(t => t.SourceBuiltinKey == src.Key)) return MatchKind.BySourceKey;
                var hit = mine.FirstOrDefault(t => Same(t.Label, src.Title));
                if (hit is null) return MatchKind.None;
                hit.SourceBuiltinKey ??= src.Key;
                return MatchKind.ByTitle;
            }
            default:
                return MatchKind.None;
        }
    }

    private void InsertCopy(Guid doctorId, string kind, BuiltinSource src, DateTimeOffset now)
    {
        switch (kind)
        {
            case BuiltInTemplateKind.Soap:
                var soap = src.Soap!;
                db.SoapTemplates.Add(new SoapTemplate
                {
                    Id = Guid.NewGuid(), DoctorId = doctorId, Title = soap.Title, IsSystemTemplate = false,
                    SourceBuiltinKey = src.Key, ChiefComplaint = soap.ChiefComplaint, Subjective = soap.Subjective,
                    Objective = soap.Objective, Assessment = soap.Assessment, Plan = soap.Plan,
                    CreatedAt = now, UpdatedAt = now,
                });
                break;
            case BuiltInTemplateKind.Rx:
                var rx = src.Rx!;
                var copyId = Guid.NewGuid();
                db.PrescriptionTemplates.Add(new PrescriptionTemplate
                {
                    TemplateId = copyId, DoctorId = doctorId, Title = rx.Title, IsSystemTemplate = false,
                    SourceBuiltinKey = src.Key, CreatedAt = now, UpdatedAt = now,
                });
                foreach (var i in rx.Items)
                {
                    db.PrescriptionTemplateItems.Add(new PrescriptionTemplateItem
                    {
                        Id = Guid.NewGuid(), TemplateId = copyId, MedicineId = i.MedicineId,
                        GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
                        Instruction = i.Instruction, IsControlledSubstance = i.IsControlledSubstance,
                        Timing = i.Timing, MealRelation = i.MealRelation, DurationKind = i.DurationKind,
                        DurationValue = i.DurationValue, Indication = i.Indication, Route = i.Route,
                        CreatedAt = now,
                    });
                }
                break;
            case BuiltInTemplateKind.Cert:
                var cert = src.Cert!;
                db.MedicalCertificateTemplates.Add(new MedicalCertificateTemplate
                {
                    Id = Guid.NewGuid(), DoctorId = doctorId, Title = cert.Title, IsSystemTemplate = false,
                    SourceBuiltinKey = src.Key, DiagnosisText = cert.DiagnosisText,
                    Recommendations = cert.Recommendations, PurposeException = cert.PurposeException,
                    CreatedAt = now, UpdatedAt = now,
                });
                break;
            case BuiltInTemplateKind.Diagnosis:
                var dx = src.Diagnosis!;
                db.DoctorDiagnosisTemplates.Add(new DoctorDiagnosisTemplate
                {
                    Id = Guid.NewGuid(), DoctorId = doctorId, Label = dx.Label, Body = dx.Body,
                    SourceBuiltinKey = src.Key, CreatedAt = now, UpdatedAt = now,
                });
                break;
            case BuiltInTemplateKind.Phrase:
                var ph = src.Phrase!;
                db.SoapPhrases.Add(new SoapPhrase
                {
                    Id = Guid.NewGuid(), DoctorId = doctorId, Field = ph.Field, Label = ph.Label, Body = ph.Body,
                    SourceBuiltinKey = src.Key, CreatedAt = now, UpdatedAt = now,
                });
                break;
        }
    }

    private async Task EnsureTrackingAsync(Guid doctorId, string kind, string key, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await db.DoctorBuiltinTemplateCopies.AnyAsync(
            c => c.DoctorId == doctorId && c.Kind == kind && c.BuiltinKey == key, ct);
        if (exists) return;
        db.DoctorBuiltinTemplateCopies.Add(new DoctorBuiltinTemplateCopy
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, Kind = kind, BuiltinKey = key, CopiedAt = now,
        });
        await TrySaveAsync(ct);
    }

    private async Task TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
    }

    internal static bool IsDuplicateKey(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("2601") || m.Contains("2627"))
                return true;
        }
        return false;
    }

    private sealed class BuiltinSource
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public SoapTemplate? Soap { get; init; }
        public PrescriptionTemplate? Rx { get; init; }
        public MedicalCertificateTemplate? Cert { get; init; }
        public BuiltInDiagnosisDef? Diagnosis { get; init; }
        public BuiltInPhraseDef? Phrase { get; init; }

        public static BuiltinSource FromSoap(SoapTemplate t) => new()
        {
            Key = t.Id.ToString(), Title = t.Title, Soap = t,
        };
        public static BuiltinSource FromRx(PrescriptionTemplate t) => new()
        {
            Key = t.TemplateId.ToString(), Title = t.Title, Rx = t,
        };
        public static BuiltinSource FromCert(MedicalCertificateTemplate t) => new()
        {
            Key = t.Id.ToString(), Title = t.Title, Cert = t,
        };
        public static BuiltinSource FromDiagnosis(BuiltInDiagnosisDef d) => new()
        {
            Key = d.Key, Title = d.Label, Diagnosis = d,
        };
        public static BuiltinSource FromPhrase(BuiltInPhraseDef p) => new()
        {
            Key = p.Key, Title = p.Label, Phrase = p,
        };
    }
}
//JJQR - END
