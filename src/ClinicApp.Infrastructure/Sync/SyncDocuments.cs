//JJQR - AI (Nov 21, 2026) cloud sync: the JSON document of each aggregate (same shape for push and pull)
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure.Sync;

/// <summary>Builds <c>data</c> for an aggregate from the current rows. The Clinic Node can call
/// the same method on its local SQLite <see cref="ClinicAppDbContext"/> to build the push payload.</summary>
public static class SyncDocuments
{
    public sealed record Doc(object Data, long RowVersion);

    /// <summary>Null when the aggregate no longer exists (pull emits a delete), or when the row is
    /// never sent to nodes (patient-portal accounts).</summary>
    public static async Task<Doc?> BuildAsync(ClinicAppDbContext db, string aggregate, string entityId, CancellationToken ct = default)
    {
        if (SyncAggregates.PullOnly.TryGetValue(aggregate, out var clr))
            return await BuildPullOnlyAsync(db, aggregate, clr, entityId, ct);
        if (!Guid.TryParse(entityId, out var id)) return null;
        switch (aggregate)
        {
            case SyncAggregates.Patient:
                {
                    var p = await db.Patients.AsNoTracking().FirstOrDefaultAsync(x => x.PatientId == id, ct);
                    if (p is null) return null;
                    var row = SyncRows.ToRow(db, p);
                    //JJQR - AI (Nov 21, 2026) allergies travel with the patient (replace-set; see contract §5.1)
                    var allergies = await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == id).OrderBy(a => a.Allergen).ThenBy(a => a.Id).ToListAsync(ct);
                    row["allergies"] = allergies.Select(a => SyncRows.ToRow(db, a)).ToList();
                    //JJQR - END
                    //JJQR - AI (21 Nov 2026) vaccinations with no consultation (pull side only; the push engine ignores this key)
                    var standaloneVax = await db.PatientVaccinations.AsNoTracking().Where(v => v.PatientId == id && v.ConsultationId == null).OrderBy(v => v.CreatedAt).ThenBy(v => v.Id).ToListAsync(ct);
                    row["vaccinations"] = standaloneVax.Select(v => SyncRows.ToRow(db, v)).ToList();
                    //JJQR - END
                    return new Doc(row, p.RowVersion);
                }
            case SyncAggregates.Booking:
                {
                    var b = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == id, ct);
                    if (b is null) return null;
                    var services = await db.BookingServices.AsNoTracking().Where(x => x.BookingId == id).OrderBy(x => x.ServiceId).ToListAsync(ct);
                    return new Doc(new Dictionary<string, object?>
                    {
                        ["booking"] = SyncRows.ToRow(db, b),
                        ["booking_services"] = services.Select(s => SyncRows.ToRow(db, s)).ToList(),
                    }, b.RowVersion);
                }
            case SyncAggregates.Payment:
                {
                    var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.PaymentId == id, ct);
                    return p is null ? null : new Doc(SyncRows.ToRow(db, p), p.RowVersion);
                }
            case SyncAggregates.Consultation:
                return await BuildConsultationAsync(db, id, ct);
            case SyncAggregates.VitalReading:
                {
                    var r = await db.PatientVitalReadings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                    return r is null ? null : new Doc(SyncRows.ToRow(db, r), r.RowVersion);
                }
            case SyncAggregates.AuditLog:
                {
                    var a = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                    return a is null ? null : new Doc(SyncRows.ToRow(db, a), a.RowVersion);
                }
        }
        return null;
    }

    private static async Task<Doc?> BuildConsultationAsync(ClinicAppDbContext db, Guid id, CancellationToken ct)
    {
        var c = await db.Consultations.AsNoTracking().FirstOrDefaultAsync(x => x.ConsultationId == id, ct);
        if (c is null) return null;
        var diagnoses = await db.ConsultationDiagnoses.AsNoTracking().Where(x => x.ConsultationId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var group = await db.PrescriptionGroups.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == c.BookingId, ct);
        var lines = group is null ? [] : await db.PrescriptionLineItems.AsNoTracking().Where(x => x.GroupId == group.GroupId).ToListAsync(ct);
        var labs = await db.LabOrders.AsNoTracking().Where(x => x.ConsultationId == id).ToListAsync(ct);
        var vaccinations = await db.PatientVaccinations.AsNoTracking().Where(x => x.ConsultationId == id).ToListAsync(ct);
        var followUp = await db.FollowUps.AsNoTracking().FirstOrDefaultAsync(x => x.ConsultationId == id, ct);
        var cert = await db.MedicalCertificates.AsNoTracking().FirstOrDefaultAsync(x => x.ConsultationId == id, ct);
        return new Doc(new Dictionary<string, object?>
        {
            ["consultation"] = SyncRows.ToRow(db, c),
            ["diagnoses"] = diagnoses.Select(x => SyncRows.ToRow(db, x)).ToList(),
            ["prescription"] = group is null ? null : new Dictionary<string, object?>
            {
                ["group"] = SyncRows.ToRow(db, group),
                ["line_items"] = lines.Select(x => SyncRows.ToRow(db, x)).ToList(),
            },
            ["lab_orders"] = labs.Select(x => SyncRows.ToRow(db, x)).ToList(),
            ["vaccinations"] = vaccinations.Select(x => SyncRows.ToRow(db, x)).ToList(),
            ["follow_up"] = followUp is null ? null : SyncRows.ToRow(db, followUp),
            ["medical_certificate"] = cert is null ? null : SyncRows.ToRow(db, cert),
        }, c.RowVersion);
    }

    /// <summary>Marker returned for rows that exist but must never be sent (patient accounts).</summary>
    public static readonly Doc Hidden = new(new object(), -1);

    private static async Task<Doc?> BuildPullOnlyAsync(ClinicAppDbContext db, string aggregate, Type clr, string entityId, CancellationToken ct)
    {
        var type = db.Model.FindEntityType(clr)!;
        var key = SyncAggregates.ParseKey(type, entityId);
        if (key is null) return null;
        var entity = await db.FindAsync(clr, key, ct);
        if (entity is null) return null;

        // Offline login needs staff / doctor / admin accounts only — never patient-portal accounts.
        if (entity is ClinicApp.Auth.User u)
        {
            var isStaff = await db.StaffAccounts.AsNoTracking().AnyAsync(s => s.UserId == u.Id, ct);
            if (!isStaff) return Hidden;
        }
        if (entity is Profile pr && pr.Role == UserRole.Patient) return Hidden;

        var row = SyncRows.ToRow(db, entity);
        if (entity is PrescriptionTemplate t)
        {
            var items = await db.PrescriptionTemplateItems.AsNoTracking().Where(i => i.TemplateId == t.TemplateId).ToListAsync(ct);
            row["items"] = items.Select(i => SyncRows.ToRow(db, i)).ToList();
        }
        return new Doc(row, 0);
    }
}
//JJQR - END
