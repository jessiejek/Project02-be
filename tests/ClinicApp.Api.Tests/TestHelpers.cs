// JJQR - AI (Nov 20, 2026) - finish/all: shared helpers for tests that need a real password login
using ClinicApp.Auth;
using ClinicApp.Domain.Enums;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>A brand-new patient login (so a test can book without touching the seeded patients).</summary>
    public static async Task<(Guid UserId, Guid PatientId)> NewPatientAsync(ApiFactory f, string? first = null)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, Email = $"pt-{id:N}@test.local", PasswordHash = "x", EmailConfirmed = true, CreatedAt = now });
        db.Profiles.Add(new Profile { Id = id, Role = UserRole.Patient, CreatedAt = now });
        db.Patients.Add(new Patient
        {
            PatientId = id, UserId = id, PatientCode = $"PF-{id.ToString("N")[..8]}", FirstName = first ?? "Pat", LastName = "Tester",
            DateOfBirth = new DateOnly(1990, 1, 1), Sex = SexType.Female, Email = $"pt-{id:N}@test.local", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return (id, id);
    }

    /// <summary>The doctor <c>POST /api/bookings/book</c> assigns (the first doctor by id).</summary>
    public static async Task<Guid> BookingDoctorAsync(ApiFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
        return await db.Doctors.OrderBy(d => d.DoctorId).Select(d => d.DoctorId).FirstAsync();
    }

    /// <summary>The next date on/after 2031-01-01 that falls on the given weekday (far future, clear of other tests).</summary>
    public static DateOnly FutureDate(DayOfWeek dow, int weeksAhead = 0)
    {
        var d = new DateOnly(2031, 1, 1);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        return d.AddDays(7 * weeksAhead);
    }
}
// JJQR - END
