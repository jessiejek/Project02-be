using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/patients")]
public class PatientsController(ClinicAppDbContext db) : ControllerBase
{
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpGet]
    public async Task<ActionResult<List<Patient>>> GetAll([FromQuery] string? search, CancellationToken ct)
    {
        var query = db.Patients.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p =>
                p.FirstName.Contains(s) || p.LastName.Contains(s) ||
                p.PatientCode.Contains(s) || p.Email.Contains(s));
        }

        return Ok(await query.OrderBy(p => p.LastName).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Patient>> GetById(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == id, ct);
        return patient is null ? NotFound() : Ok(patient);
    }

    /// <summary>The logged-in patient's own row.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<Patient>> GetMine(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);
        return patient is null ? NotFound() : Ok(patient);
    }

    [HttpGet("me/vaccinations")]
    public async Task<ActionResult<List<PatientVaccination>>> GetMyVaccinations(CancellationToken ct)
    {
        var patientId = await CurrentPatientIdAsync(ct);
        if (patientId is null) return Forbid();

        return Ok(await db.PatientVaccinations.AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.AdministeredDate)
            .ToListAsync(ct));
    }

    [HttpGet("{id:guid}/vaccinations")]
    [Authorize(Roles = "Admin,Staff,Doctor")]
    public async Task<ActionResult<List<PatientVaccination>>> GetVaccinations(Guid id, CancellationToken ct) =>
        Ok(await db.PatientVaccinations.AsNoTracking().Where(v => v.PatientId == id).OrderByDescending(v => v.AdministeredDate).ToListAsync(ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Patient payload, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();

        patient.FirstName = payload.FirstName;
        patient.MiddleName = payload.MiddleName;
        patient.LastName = payload.LastName;
        patient.DateOfBirth = payload.DateOfBirth;
        patient.Sex = payload.Sex;
        patient.CivilStatus = payload.CivilStatus;
        patient.Address = payload.Address;
        patient.City = payload.City;
        patient.ZipCode = payload.ZipCode;
        patient.ContactNumber = payload.ContactNumber;
        patient.EmergencyContactName = payload.EmergencyContactName;
        patient.EmergencyContactNumber = payload.EmergencyContactNumber;
        patient.EmergencyContactRelationship = payload.EmergencyContactRelationship;
        patient.BloodType = payload.BloodType;
        patient.PhilhealthNumber = payload.PhilhealthNumber;
        patient.HmoProvider = payload.HmoProvider;
        patient.HmoCardNumber = payload.HmoCardNumber;

        await db.SaveChangesAsync(ct);
        return Ok(patient);
    }

    /// <summary>PATCH-style consent update per contract §10.</summary>
    [HttpPut("{id:guid}/consent")]
    public async Task<IActionResult> UpdateConsent(Guid id, [FromBody] int consentVersion, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();

        patient.ConsentVersion = consentVersion;
        patient.ConsentedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(patient);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private async Task<Guid?> CurrentPatientIdAsync(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;
        return await db.Patients.Where(p => p.UserId == userId).Select(p => (Guid?)p.PatientId).SingleOrDefaultAsync(ct);
    }
}
