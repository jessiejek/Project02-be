using ClinicApp.Domain.Entities;
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
        return row is null ? NotFound() : Ok(row);
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

    /// <summary>Editable fields per contract §10 (status, full_name, contact_number).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, StaffAccount payload, CancellationToken ct)
    {
        var row = await db.StaffAccounts.SingleOrDefaultAsync(s => s.StaffId == id, ct);
        if (row is null) return NotFound();

        var self = CurrentUserId() == row.UserId;
        if (!self && !(User.IsInRole("Admin") || User.IsInRole("Staff")))
            return Forbid();

        row.FullName = payload.FullName;
        row.ContactNumber = payload.ContactNumber;
        // Only Admin/Staff may change status; a self-edit keeps the existing value.
        if (!self && (User.IsInRole("Admin") || User.IsInRole("Staff")))
            row.Status = payload.Status;

        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
