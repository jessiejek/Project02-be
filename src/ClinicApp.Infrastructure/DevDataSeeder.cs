using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Development-only. Creates the five login accounts from the Project02-fe
/// <c>account-list.txt</c> — <c>users</c> + <c>profiles</c> only, with the
/// <b>same ids and roles as the Supabase project</b> and a shared password
/// (<see cref="DevPassword"/>).
///
/// The domain rows (<c>staff_accounts</c>, <c>doctors</c>, <c>patients</c>, …)
/// are NOT seeded here — they are mirrored from Supabase with their real primary
/// keys by <c>Project02-fe/scripts/dev/import-from-supabase.mjs</c> →
/// <see cref="ClinicApp.Api"/> <c>POST /api/dev/import</c>. Run that once after
/// first startup. Until then login still works (the session falls back to the
/// email as the display name).
///
/// Idempotent: skips any user whose email already exists. Never runs outside
/// the Development environment (guarded in Program.cs).
/// </summary>
public static class DevDataSeeder
{
    public const string DevPassword = "ClinicDev123!";

    private static readonly (Guid Id, string Email, UserRole Role)[] Accounts =
    [
        (Guid.Parse("409a0fd7-c371-4c4c-89b3-31f9d3b18366"), "admin@clinic.test",       UserRole.Admin),
        (Guid.Parse("aa4f8351-410c-4b5d-842f-e684de014276"), "staff@clinic.test",       UserRole.Staff),
        (Guid.Parse("60310394-4c74-4370-9cd3-47b22c4cbfb8"), "doctor@clinic.test",      UserRole.Doctor),
        (Guid.Parse("857757af-9545-485b-8981-5a2043f54fe9"), "phase5doctor@clinic.test", UserRole.Doctor),
        (Guid.Parse("f3c7d22e-9f67-4511-ae1a-73b30b2fc4e6"), "patient@clinic.test",     UserRole.Patient),
    ];

    public static async Task SeedAsync(ClinicAppDbContext db, PasswordHasherService hasher, ILogger logger, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var created = 0;

        foreach (var (id, email, role) in Accounts)
        {
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) continue;

            var user = new User { Id = id, Email = email, EmailConfirmed = true, MustSetPassword = false, CreatedAt = now };
            user.PasswordHash = hasher.Hash(user, DevPassword);
            db.Users.Add(user);
            db.Profiles.Add(new Profile { Id = id, Role = role, CreatedAt = now });
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "DevDataSeeder: created {Count} login account(s), password '{Password}'. " +
                "Run scripts/dev/import-from-supabase.mjs to populate domain rows.",
                created, DevPassword);
        }
    }
}
