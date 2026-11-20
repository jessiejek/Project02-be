// JJQR - AI (Nov 20, 2026) - finish/all: shared helpers for tests that need a real password login
using ClinicApp.Auth;
using ClinicApp.Domain.Enums;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;

namespace ClinicApp.Api.Tests;

public static class TestHelpers
{
    public const string TestPassword = "Correct-Horse-Battery-9";

    /// <summary>A new staff-like account with a real password hash (so POST /api/auth/login works).</summary>
    public static async Task<(Guid UserId, Guid StaffId, string Email)> NewStaffLoginAsync(
        ApiFactory f, UserRole role = UserRole.Staff, StaffStatus status = StaffStatus.Active, string? password = null)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasherService>();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var email = $"login-{id:N}@test.local";
        var user = new User { Id = id, Email = email, EmailConfirmed = true, CreatedAt = now };
        user.PasswordHash = hasher.Hash(user, password ?? TestPassword);
        db.Users.Add(user);
        db.Profiles.Add(new Profile { Id = id, Role = role, CreatedAt = now });
        var staffRole = role switch { UserRole.Admin => StaffRole.Admin, UserRole.Doctor => StaffRole.Doctor, _ => StaffRole.Staff };
        var staffId = Guid.NewGuid();
        db.StaffAccounts.Add(new StaffAccount
        {
            StaffId = staffId, UserId = id, FullName = $"Login {staffId.ToString()[..4]}", Email = email,
            Role = staffRole, Status = status, InvitedAt = now, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (id, staffId, email);
    }
}
// JJQR - END
