using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicApp.Infrastructure;

/// <summary>§17.1 #1 — `patient_code` is issued by the server, never by a client. Format
/// `MF-000123` (zero-padded, monotonic). The unique index `ix_patients_patient_code` is the
/// final arbiter; <see cref="PatientCodes.SaveWithFreshCodeAsync"/> retries if two writers ever
/// race to the same code, so a collision can never surface as a 500 or a half-saved row.</summary>
public interface IPatientCodeAllocator
{
    /// <summary>The next unused code, e.g. `MF-100042`. Cheap and safe to call concurrently.</summary>
    Task<string> NextAsync(ClinicAppDbContext db, CancellationToken ct = default);
}

public sealed class PatientCodeAllocator : IPatientCodeAllocator
{
    public const string Prefix = "MF-";
    private const long SqliteFirst = 100000; // matches the SQL Server sequence START WITH

    public async Task<string> NextAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // Dev/demo/test provider only — no sequences. MAX+1 is not atomic across
                // connections, which is exactly what the unique index + retry cover.
                // Only pure-digit `MF-` codes count (legacy `MF-1234`, `P-…` and hand-typed codes
                // are ignored for numbering), and the floor keeps us at/after 100000.
                cmd.CommandText =
                    "SELECT MAX(" + (SqliteFirst - 1) + ", COALESCE(MAX(CAST(SUBSTR(patient_code, 4) AS INTEGER)), 0)) + 1 " +
                    "FROM patients WHERE patient_code GLOB 'MF-[0-9]*' AND SUBSTR(patient_code, 4) NOT GLOB '*[^0-9]*'";
            }
            else
            {
                // `NEXT VALUE FOR` can't run inside EF's SqlQuery wrapper, so use the connection.
                // The sequence (migration Phase91PatientCodeSequence) is atomic and never reissues.
                cmd.CommandText = "SELECT NEXT VALUE FOR patient_code_seq";
            }
            var result = await cmd.ExecuteScalarAsync(ct);
            return $"{Prefix}{Convert.ToInt64(result):D6}";
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }
}

public static class PatientCodes
{
    /// <summary>True when the failure is the `patient_code` unique index (SQL Server 2601/2627 or
    /// SQLite "UNIQUE constraint failed: patients.patient_code").</summary>
    public static bool IsCodeCollision(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("patient_code", StringComparison.OrdinalIgnoreCase) &&
                (m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) || m.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    /// <summary>Allocates a code, lets <paramref name="stage"/> add the patient (and anything that
    /// must be saved with it) to <paramref name="db"/>, and saves. If the unique index rejects the
    /// code the change tracker is cleared and a fresh code is tried — up to <paramref name="attempts"/>
    /// times. Returns the code that was stored. Any other failure propagates untouched.</summary>
    public static async Task<string> SaveWithFreshCodeAsync(
        ClinicAppDbContext db, IPatientCodeAllocator allocator, Action<string> stage,
        CancellationToken ct = default, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            var code = await allocator.NextAsync(db, ct);
            stage(code);
            try
            {
                await db.SaveChangesAsync(ct);
                return code;
            }
            catch (DbUpdateException ex) when (attempt < attempts && IsCodeCollision(ex))
            {
                db.ChangeTracker.Clear(); // drop the rejected rows; `stage` re-adds them next time
            }
        }
    }
}
