using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/audit-logs")]
public class AuditLogsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AuditLog>>> GetAll([FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok(await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.PerformedAt).Take(take).ToListAsync(ct));
}
