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

    /// <summary>§16.2 — server-side paged + searched patient list. `sort` is one of
    /// name | code | created (prefix "-" for descending; default "name").</summary>
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<Patient>>> Search(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? sort = "name",
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Patients.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p =>
                p.FirstName.Contains(s) || p.LastName.Contains(s) ||
                p.PatientCode.Contains(s) || p.Email.Contains(s) ||
                (p.ContactNumber != null && p.ContactNumber.Contains(s)));
        }

        var desc = sort is not null && sort.StartsWith('-');
        var key = (sort ?? "name").TrimStart('-');
        query = (key, desc) switch
        {
            ("code", false) => query.OrderBy(p => p.PatientCode),
            ("code", true) => query.OrderByDescending(p => p.PatientCode),
            ("created", false) => query.OrderBy(p => p.CreatedAt),
            ("created", true) => query.OrderByDescending(p => p.CreatedAt),
            (_, true) => query.OrderByDescending(p => p.LastName).ThenByDescending(p => p.FirstName),
            _ => query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new PagedResult<Patient> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Patient>> GetById(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == id, ct);
        return patient is null ? NotFound() : Ok(patient);
    }

    /// <summary>Create a patient record (guest / staff quick-register). No auth
    /// account is linked here — contract §9 keeps that a separate layer.</summary>
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpPost]
    public async Task<ActionResult<Patient>> Create(Patient payload, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        payload.PatientId = payload.PatientId == Guid.Empty ? Guid.NewGuid() : payload.PatientId;
        payload.UserId = null;
        payload.CreatedAt = now;
        payload.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(payload.PatientCode))
        {
            // §17.1 #1 — server-side monotonic code. The old client/random
            // `MF-{1000..9999}` had only 9000 values and no retry, so inserts
            // started failing at ~110 patients. `NEXT VALUE FOR` can't run inside
            // EF's SqlQuery wrapper, so hit the connection directly.
            payload.PatientCode = $"MF-{await NextPatientCodeAsync(ct):D6}";
        }

        db.Patients.Add(payload);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = payload.PatientId }, payload);
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

    private async Task<long> NextPatientCodeAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT NEXT VALUE FOR patient_code_seq";
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    private async Task<Guid?> CurrentPatientIdAsync(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;
        return await db.Patients.Where(p => p.UserId == userId).Select(p => (Guid?)p.PatientId).SingleOrDefaultAsync(ct);
    }
}
