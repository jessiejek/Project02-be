// JJQR - AI (Nov 20, 2026) - PV-5: "who looked at this patient" — every staff/doctor/admin read of patient data is written down (RA 10173 accountability)
using ClinicApp.Api.Security;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Auditing;

public static class AccessLog
{
    /// <summary>The same person opening the same area of the same patient again within this window is one entry, so the log stays readable.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Records that <paramref name="actor"/> viewed <paramref name="area"/> of patient <paramref name="patientId"/>.
    /// Patients reading their own data are not logged. The entry holds ids and an area name only — never the patient's details.</summary>
    public static async Task RecordAsync(ClinicAppDbContext db, Actor actor, Guid patientId, string area, CancellationToken ct)
    {
        if (!actor.IsStaffLike || actor.UserId is not { } uid || patientId == Guid.Empty) return;
        var since = DateTimeOffset.UtcNow - Window;
        var again = await db.AuditLogs.AnyAsync(a =>
            a.EntityType == AuditEntityType.Access && a.EntityId == patientId && a.PerformedByUserId == uid && a.Details == area && a.PerformedAt > since, ct);
        if (again) return;
        AuditLogWriter.Add(db, AuditEntityType.Access, patientId, "Viewed", uid, area);
        await db.SaveChangesAsync(ct);
    }
}
// JJQR - END
