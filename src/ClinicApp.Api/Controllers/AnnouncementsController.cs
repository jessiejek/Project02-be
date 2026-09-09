using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Route("api/announcements")]
public class AnnouncementsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Announcement>>> GetAll([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var query = db.Announcements.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(a => a.IsActive);
        return Ok(await query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Announcement>> Create(Announcement payload, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        payload.Id = Guid.NewGuid();
        payload.PostedByUserId = CurrentUserId();
        payload.CreatedAt = now;
        payload.UpdatedAt = now;

        db.Announcements.Add(payload);
        await db.SaveChangesAsync(ct);
        return Ok(payload);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Announcement payload, CancellationToken ct)
    {
        var announcement = await db.Announcements.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (announcement is null) return NotFound();

        announcement.Title = payload.Title;
        announcement.Body = payload.Body;
        announcement.IsActive = payload.IsActive;

        await db.SaveChangesAsync(ct);
        return Ok(announcement);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var announcement = await db.Announcements.FindAsync([id], ct);
        if (announcement is null) return NotFound();
        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
