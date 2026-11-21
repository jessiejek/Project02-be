//JJQR - AI (Nov 21, 2026) cloud sync: client-supplied ids on REST creates (offline nodes create their own GUIDs)
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Sync;

public static class ClientIds
{
    public static bool Supplied(Guid? requested) => requested is Guid g && g != Guid.Empty;

    /// <summary>The client's id when supplied, else a new one (old behaviour).</summary>
    public static Guid Use(Guid? requested) => Supplied(requested) ? requested!.Value : Guid.NewGuid();

    /// <summary>True when the client supplied an id that already belongs to a row of <typeparamref name="T"/>.</summary>
    public static async Task<bool> TakenAsync<T>(ClinicAppDbContext db, Guid? requested, CancellationToken ct) where T : class
    {
        if (!Supplied(requested)) return false;
        var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
        var id = requested!.Value;
        return await db.Set<T>().AsNoTracking().AnyAsync(e => EF.Property<Guid>(e, key) == id, ct);
    }

    /// <summary>For "replace the whole list" endpoints: the client's id unless it is already used
    /// (in the database or earlier in the same request), in which case a new id is issued.</summary>
    public static async Task<Guid> FreshAsync<T>(ClinicAppDbContext db, Guid? requested, CancellationToken ct) where T : class
    {
        if (!Supplied(requested)) return Guid.NewGuid();
        var key = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
        var id = requested!.Value;
        if (db.ChangeTracker.Entries<T>().Any(e => Equals(e.Property(key).CurrentValue, id))) return Guid.NewGuid();
        if (await db.Set<T>().AsNoTracking().AnyAsync(e => EF.Property<Guid>(e, key) == id, ct)) return Guid.NewGuid();
        return id;
    }

    public static ObjectResult Conflict() =>
        new(new { error = "id_conflict", message = "That id is already used by another record." }) { StatusCode = 409 };
}
//JJQR - END
