using System.Security.Claims;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Security;

/// <summary>Who is calling, resolved from the (server-validated) JWT plus their
/// own patient / staff row. This is the .NET replacement for the Supabase RLS
/// helpers `current_patient_id()`, `current_doctor_id()`, `is_staff_like()`,
/// `is_admin()` — see docs/AUTHZ_MATRIX.md for the rules built on top of it.</summary>
public sealed record Actor(Guid? UserId, string? Role, Guid? PatientId, Guid? StaffId)
{
    public bool IsPatient => Role == "Patient";
    public bool IsAdmin => Role == "Admin";
    public bool IsDoctor => Role == "Doctor";
    public bool IsStaff => Role == "Staff";

    /// <summary>Admin / Staff / Doctor — RLS `is_staff_like()`.</summary>
    public bool IsStaffLike => Role is "Admin" or "Staff" or "Doctor";

    /// <summary>Own row for a patient; any row for staff-like roles; nothing otherwise.</summary>
    public bool CanAccessPatient(Guid patientId) =>
        IsStaffLike || (IsPatient && PatientId is { } own && own == patientId);

    /// <summary>The caller may act as this doctor: Admin, or the doctor themself.
    /// (`staff_accounts.staff_id` doubles as `doctors.doctor_id`.)</summary>
    public bool ActsAsDoctor(Guid doctorId) =>
        IsAdmin || (IsDoctor && StaffId is { } own && own == doctorId);
}

/// <summary>Per-request cache of the <see cref="Actor"/> so several ownership
/// checks in one request cost a single lookup.</summary>
public sealed class ActorResolver(ClinicAppDbContext db)
{
    private Actor? _actor;

    public async Task<Actor> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (_actor is not null) return _actor;

        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        Guid? userId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        Guid? patientId = null;
        Guid? staffId = null;

        if (userId is not null)
        {
            if (role == "Patient")
            {
                patientId = await db.Patients.AsNoTracking()
                    .Where(p => p.UserId == userId)
                    .Select(p => (Guid?)p.PatientId)
                    .SingleOrDefaultAsync(ct);
            }
            else if (role is "Doctor" or "Staff" or "Admin")
            {
                staffId = await db.StaffAccounts.AsNoTracking()
                    .Where(s => s.UserId == userId)
                    .Select(s => (Guid?)s.StaffId)
                    .SingleOrDefaultAsync(ct);
            }
        }

        return _actor = new Actor(userId, role, patientId, staffId);
    }
}
