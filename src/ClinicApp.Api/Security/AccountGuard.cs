// JJQR - AI (Nov 20, 2026) - B-32: one place that decides whether an account may use the API
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Security;

/// <summary>An account may log in / keep using its tokens only while its user row exists and, for
/// staff / doctor / admin accounts, its staff_accounts row is not <see cref="StaffStatus.Inactive"/>
/// (the admin's "Deactivate").</summary>
public static class AccountGuard
{
    public const string DeactivatedMessage = "This account has been deactivated. Please contact the clinic administrator.";

    public static async Task<bool> IsActiveAsync(ClinicAppDbContext db, Guid userId, CancellationToken ct = default)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct)) return false;
        var status = await db.StaffAccounts.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => (StaffStatus?)s.Status)
            .FirstOrDefaultAsync(ct);
        return status != StaffStatus.Inactive;
    }
}
// JJQR - END
