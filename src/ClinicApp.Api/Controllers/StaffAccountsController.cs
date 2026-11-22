using ClinicApp.Api.Validation;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>snake_case wire format (contract §4). staff_accounts covers Staff /
/// Doctor / Admin under one row (admin Staff Management lists all three).</summary>
[ApiController]
[Authorize]
[Route("api/staff-accounts")]
public class StaffAccountsController(ClinicAppDbContext db) : ControllerBase
{
    /// <summary>List, optionally filtered by role (?role=Staff|Doctor|Admin).</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpGet]
    public async Task<ActionResult<List<StaffAccount>>> GetAll([FromQuery] string? role, CancellationToken ct)
    {
        var query = db.StaffAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<Domain.Enums.StaffRole>(role, out var r))
            query = query.Where(s => s.Role == r);

        return Ok(await query.OrderBy(s => s.FullName).ToListAsync(ct));
    }

    /// <summary>§16.2 — paged + searched staff list. `q` matches name / email;
    /// `sort` = name | role | status (prefix "-" = descending; default "name").</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<StaffAccount>>> Search(
        [FromQuery] string? q, [FromQuery] string? role,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? sort = "name", CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.StaffAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<Domain.Enums.StaffRole>(role, out var r))
            query = query.Where(s => s.Role == r);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(x => x.FullName.Contains(s) || x.Email.Contains(s));
        }

        var desc = sort is not null && sort.StartsWith('-');
        var key = (sort ?? "name").TrimStart('-');
        query = (key, desc) switch
        {
            ("role", false) => query.OrderBy(x => x.Role),
            ("role", true) => query.OrderByDescending(x => x.Role),
            ("status", false) => query.OrderBy(x => x.Status),
            ("status", true) => query.OrderByDescending(x => x.Status),
            (_, true) => query.OrderByDescending(x => x.FullName),
            _ => query.OrderBy(x => x.FullName),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResult<StaffAccount> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StaffAccount>> GetById(Guid id, CancellationToken ct)
    {
        var row = await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(s => s.StaffId == id, ct);
        if (row is null) return NotFound();

        // RLS staff_accounts_select_doctors_or_staff: staff-like roles read any row; everyone else
        // (patients) only an active Doctor's row (the doctor browse pages) or their own.
        var isStaffLike = User.IsInRole("Admin") || User.IsInRole("Staff") || User.IsInRole("Doctor");
        var isPublicDoctor = row.Role == StaffRole.Doctor && row.Status != StaffStatus.Inactive;
        // JJQR - AI (Nov 20, 2026) - PV-2: a patient looking at a doctor's card gets the name and photo, not the doctor's personal email / phone (same rule as the public doctor catalog)
        var self = CurrentUserId() == row.UserId;
        if (!(isStaffLike || isPublicDoctor || self)) return NotFound();
        if (!isStaffLike && !self) { row.Email = ""; row.ContactNumber = null; }
        return Ok(row);
        // JJQR - END
    }

    /// <summary>The logged-in staff/doctor/admin's own row (session, profile page).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<StaffAccount>> GetMine(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var row = await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    //JJQR - AI (21 Nov 2026) desktop: self-only profile edit (name, phone, avatar); role/status/email are never touched here
    public sealed record UpdateMyProfileRequest(
        string? FullName,
        [property: System.Text.Json.Serialization.JsonPropertyName("phone")] string? Phone,
        string? AvatarUrl);

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMine(UpdateMyProfileRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var row = await db.StaffAccounts.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null) return NotFound();

        var probe = new StaffAccount { FullName = request.FullName?.Trim() ?? "", ContactNumber = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim() };
        if (Rules.Staff(probe).Result() is { } invalid) return invalid;
        if (request.AvatarUrl is { Length: > 2000 }) return BadRequest(new { error = "invalid_request", detail = "avatar_url is too long." });

        row.FullName = probe.FullName;
        row.ContactNumber = probe.ContactNumber;
        if (request.AvatarUrl is not null) row.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();

        await db.SaveChangesAsync(ct);
        return Ok(row);
    }
    //JJQR - END

    /// <summary>Editable fields per contract §10 (status, full_name, contact_number).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, StaffAccount payload, CancellationToken ct)
    {
        var row = await db.StaffAccounts.SingleOrDefaultAsync(s => s.StaffId == id, ct);
        if (row is null) return NotFound();

        // RLS staff_accounts_update_own_or_admin: your own row, or Admin for anyone's.
        var self = CurrentUserId() == row.UserId;
        var isAdmin = User.IsInRole("Admin");
        if (!self && !isAdmin) return Forbid();
        if (Rules.Staff(payload).Result() is { } invalid) return invalid; // JJQR - AI (Nov 20, 2026) - B-01

        row.FullName = payload.FullName;
        row.ContactNumber = payload.ContactNumber;
        // Only an Admin changing someone else's row may change status; a self-edit keeps it.
        if (!self && isAdmin)
        {
            row.Status = payload.Status;
            // JJQR - AI (Nov 20, 2026) - B-32: deactivating also ends every existing session
            if (payload.Status == StaffStatus.Inactive)
            {
                var live = await db.RefreshTokens.Where(t => t.UserId == row.UserId && t.RevokedAt == null).ToListAsync(ct);
                foreach (var t in live) t.RevokedAt = DateTimeOffset.UtcNow;
            }
            // JJQR - END
        }

        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    //JJQR - AI (Nov 22, 2026) - owner: the Admin can change a staff or doctor member's sign-in email (for example a typo, or a changed address). Their sessions end so they sign in again with the new email.
    public sealed record ChangeStaffEmailRequest(string Email);

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}/email")]
    public async Task<IActionResult> ChangeEmail(Guid id, ChangeStaffEmailRequest request, CancellationToken ct)
    {
        var row = await db.StaffAccounts.SingleOrDefaultAsync(s => s.StaffId == id, ct);
        if (row is null) return NotFound();
        var user = await db.Users.FindAsync([row.UserId], ct);
        if (user is null) return NotFound();

        if (new ClinicApp.Api.Validation.Check().Email("email", "Email", request.Email, required: true).Result() is { } invalid) return invalid;
        var newEmail = request.Email.Trim().ToLowerInvariant();
        if (newEmail == user.Email) return BadRequest(new { message = "That is already their email." });
        if (await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id, ct))
            return Conflict(new { message = "An account with this email already exists." });

        user.Email = newEmail;
        row.Email = newEmail;
        var live = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in live) t.RevokedAt = DateTimeOffset.UtcNow;
        ClinicApp.Api.Auditing.AuditLogWriter.Add(db, ClinicApp.Domain.Enums.AuditEntityType.Staff, row.StaffId, "EmailChangedByAdmin", CurrentUserId(), "Sign-in email changed by an admin");
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Email updated. They must sign in again with the new email.", email = newEmail });
    }
    //JJQR - END

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
