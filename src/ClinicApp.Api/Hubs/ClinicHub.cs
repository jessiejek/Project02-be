using System.Security.Claims;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Hubs;

/// <summary>
/// One hub for the screens where staleness is an actual problem — Staff
/// Queue/Dashboard, Doctor Visits/Dashboard, Staff Payments. Server pushes
/// only (no client-invocable methods yet); group membership is assigned on
/// connect from the same JWT every REST call already carries, not chosen by
/// the client.
///
/// Groups: "staff" (Admin + Staff), "doctor:{doctorId}" (that doctor's own
/// connections). A patient's own portal isn't wired to this yet — it wasn't
/// part of the staleness problem being solved.
/// </summary>
[Authorize(Roles = "Admin,Staff,Doctor")]
public sealed class ClinicHub(ClinicAppDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (role is "Admin" or "Staff")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "staff");
        }
        else if (role == "Doctor")
        {
            var userIdRaw = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdRaw, out var userId))
            {
                // StaffAccount.StaffId doubles as Doctor.DoctorId (shared PK,
                // same convention as every other staff_accounts child table).
                var staffId = await db.StaffAccounts.AsNoTracking()
                    .Where(s => s.UserId == userId)
                    .Select(s => (Guid?)s.StaffId)
                    .SingleOrDefaultAsync();
                if (staffId is not null)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"doctor:{staffId}");
                }
            }
        }
        await base.OnConnectedAsync();
    }
}
