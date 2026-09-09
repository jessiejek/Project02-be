using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>
/// doctor_services collection reads (contract §4/§6). Public — the booking flow
/// needs the full doctor↔service map pre-login. Per-doctor GET/PUT stays on
/// DoctorsController (<c>/api/doctors/{id}/services</c>).
/// </summary>
[ApiController]
[Route("api/doctor-services")]
public class DoctorServicesController(ClinicAppDbContext db) : ControllerBase
{
    /// <summary>All rows, or just one doctor's (?doctorId=). Includes the
    /// nested services(name, category, price) embed.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DoctorService>>> GetAll([FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var query = db.DoctorServices.AsNoTracking().Include(ds => ds.Service).AsQueryable();
        if (doctorId is not null) query = query.Where(ds => ds.DoctorId == doctorId);
        return Ok(await query.ToListAsync(ct));
    }
}
