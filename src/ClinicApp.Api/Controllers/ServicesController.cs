using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Route("api/services")]
public class ServicesController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Service>>> GetAll([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var query = db.Services.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(s => s.IsActive);
        return Ok(await query.OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Service>> GetById(Guid id, CancellationToken ct)
    {
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.ServiceId == id, ct);
        return service is null ? NotFound() : Ok(service);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Service>> Create(Service payload, CancellationToken ct)
    {
        payload.ServiceId = Guid.NewGuid();
        payload.CreatedAt = DateTimeOffset.UtcNow;
        payload.UpdatedAt = payload.CreatedAt;
        db.Services.Add(payload);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = payload.ServiceId }, payload);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Service payload, CancellationToken ct)
    {
        var service = await db.Services.SingleOrDefaultAsync(s => s.ServiceId == id, ct);
        if (service is null) return NotFound();

        service.Name = payload.Name;
        service.Category = payload.Category;
        service.Description = payload.Description;
        service.Price = payload.Price;
        service.IsActive = payload.IsActive;

        await db.SaveChangesAsync(ct);
        return Ok(service);
    }
}
