using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Development-only. Creates the login accounts from the Project02-fe
/// <c>account-list.txt</c> (the same emails/roles/ids as the Supabase project)
/// so the auth cutover (INTEGRATION_ROADMAP.md Phase 1) can be exercised without
/// a full data import. Every account shares the password <see cref="DevPassword"/>.
///
/// Idempotent: skips any user whose email already exists. Never runs outside
/// the Development environment (guarded in Program.cs).
/// </summary>
public static class DevDataSeeder
{
    public const string DevPassword = "ClinicDev123!";

    // Auth user ids copied verbatim from account-list.txt so `sub` claims line up
    // with the Supabase project during the dual-run migration window.
    private static readonly Guid AdminUserId   = Guid.Parse("409a0fd7-c371-4c4c-89b3-31f9d3b18366");
    private static readonly Guid StaffUserId   = Guid.Parse("aa4f8351-410c-4b5d-842f-e684de014276");
    private static readonly Guid DoctorUserId  = Guid.Parse("60310394-4c74-4370-9cd3-47b22c4cbfb8");
    private static readonly Guid Doctor5UserId = Guid.Parse("857757af-9545-485b-8981-5a2043f54fe9");
    private static readonly Guid PatientUserId = Guid.Parse("f3c7d22e-9f67-4511-ae1a-73b30b2fc4e6");

    public static async Task SeedAsync(ClinicAppDbContext db, PasswordHasherService hasher, ILogger logger, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var created = 0;

        // ── admin@clinic.test ───────────────────────────────────────────────
        if (await AddUserAsync(db, hasher, AdminUserId, "admin@clinic.test", UserRole.Admin, now, ct))
        {
            db.StaffAccounts.Add(Staff(AdminUserId, "Test Admin", "admin@clinic.test", StaffRole.Admin, StaffStatus.Active, now));
            created++;
        }

        // ── staff@clinic.test ──────────────────────────────────────────────
        if (await AddUserAsync(db, hasher, StaffUserId, "staff@clinic.test", UserRole.Staff, now, ct))
        {
            db.StaffAccounts.Add(Staff(StaffUserId, "Test Staff", "staff@clinic.test", StaffRole.Staff, StaffStatus.Active, now));
            created++;
        }

        // ── doctor@clinic.test (active) ────────────────────────────────────
        if (await AddUserAsync(db, hasher, DoctorUserId, "doctor@clinic.test", UserRole.Doctor, now, ct))
        {
            var staffId = DeterministicStaffId(DoctorUserId);
            db.StaffAccounts.Add(Staff(DoctorUserId, "Test Doctor", "doctor@clinic.test", StaffRole.Doctor, StaffStatus.Active, now, staffId));
            db.Doctors.Add(DoctorRow(staffId, "General Medicine", 500m, now));
            created++;
        }

        // ── phase5doctor@clinic.test (inactive) ───────────────────────────
        if (await AddUserAsync(db, hasher, Doctor5UserId, "phase5doctor@clinic.test", UserRole.Doctor, now, ct))
        {
            var staffId = DeterministicStaffId(Doctor5UserId);
            db.StaffAccounts.Add(Staff(Doctor5UserId, "Phase5 Doctor", "phase5doctor@clinic.test", StaffRole.Doctor, StaffStatus.Inactive, now, staffId));
            db.Doctors.Add(DoctorRow(staffId, "Dermatology", 850m, now));
            created++;
        }

        // ── patient@clinic.test (portal patient) ──────────────────────────
        if (await AddUserAsync(db, hasher, PatientUserId, "patient@clinic.test", UserRole.Patient, now, ct))
        {
            db.Patients.Add(new Patient
            {
                PatientId = DeterministicStaffId(PatientUserId),
                UserId = PatientUserId,
                PatientCode = "MF-0001",
                FirstName = "TestEdited",
                LastName = "Patient",
                DateOfBirth = new DateOnly(1990, 1, 1),
                Sex = SexType.Female,
                Email = "patient@clinic.test",
                IsGuest = false,
                IsEmailVerified = true,
                ConsentVersion = 1,
                ConsentedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("DevDataSeeder: created {Count} login account(s), password '{Password}'.", created, DevPassword);
        }
    }

    private static async Task<bool> AddUserAsync(
        ClinicAppDbContext db, PasswordHasherService hasher, Guid id, string email, UserRole role, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return false;

        var user = new User { Id = id, Email = email, EmailConfirmed = true, MustSetPassword = false, CreatedAt = now };
        user.PasswordHash = hasher.Hash(user, DevPassword);
        db.Users.Add(user);
        db.Profiles.Add(new Profile { Id = id, Role = role, CreatedAt = now });
        return true;
    }

    private static StaffAccount Staff(
        Guid userId, string fullName, string email, StaffRole role, StaffStatus status, DateTimeOffset now, Guid? staffId = null) => new()
    {
        StaffId = staffId ?? DeterministicStaffId(userId),
        UserId = userId,
        FullName = fullName,
        Email = email,
        Role = role,
        Status = status,
        InvitedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Doctor DoctorRow(Guid doctorId, string specialization, decimal fee, DateTimeOffset now) => new()
    {
        DoctorId = doctorId,
        Specialization = specialization,
        ConsultationFee = fee,
        SlotDurationMinutes = 30,
        SlotCapacity = 1,
        CreatedAt = now,
        UpdatedAt = now
    };

    // Stable per-user id for the staff_accounts/patients PK (distinct from the auth user id,
    // matching the Supabase schema where staff_id != user_id).
    private static Guid DeterministicStaffId(Guid userId)
    {
        var b = userId.ToByteArray();
        b[0] ^= 0x5A; // flip a byte so it can't collide with the source guid
        return new Guid(b);
    }
}
