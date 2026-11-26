//JJQR - AI (2026-11-26) - GAV-12 why: a merged-away patient (merged_into_patient_id + deleted_at) is never written to again. Every place that receives a patient id
// (sync push, old links) asks here for the record it now lives in. Follows a chain (A merged into B, B later into C) to the end; a loop or a too-long chain answers null.
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

public static class PatientMerges
{
    /// <summary>The kept patient's id when <paramref name="patientId"/> was merged away; null when it is a live patient (or unknown).</summary>
    public static async Task<Guid?> KeeperOfAsync(ClinicAppDbContext db, Guid patientId, CancellationToken ct = default)
    {
        Guid? keeper = null;
        var seen = new HashSet<Guid> { patientId };
        var cur = patientId;
        for (var i = 0; i < 10; i++)
        {
            var next = await db.Patients.AsNoTracking().Where(p => p.PatientId == cur).Select(p => p.MergedIntoPatientId).FirstOrDefaultAsync(ct);
            if (next is not Guid n) return keeper;
            if (!seen.Add(n)) return null;
            keeper = n; cur = n;
        }
        return null;
    }
}
//JJQR - END
