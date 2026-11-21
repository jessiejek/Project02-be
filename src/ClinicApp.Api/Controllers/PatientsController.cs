using ClinicApp.Api.Validation;
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Security;
//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/patients")]
public class PatientsController(ClinicAppDbContext db, ActorResolver actors, IPatientCodeAllocator codes) : ControllerBase
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
        // A patient may only read their own row (RLS patients_select_own_or_staff); 404 for others.
        if (!(await actors.ResolveAsync(User, ct)).CanAccessPatient(id)) return NotFound();

        var patient = await db.Patients.AsNoTracking()
            //JJQR - AI (Oct 31, 2026)
            .Include(p => p.Allergies)
            //JJQR - END
            .SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();
        await AccessLog.RecordAsync(db, await actors.ResolveAsync(User, ct), id, "Patient record", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(patient);
    }

    //JJQR - AI (21 Nov 2026) desktop: data for the printed patient card. Read-only, identity + allergy names only (no clinical notes).
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpGet("{id:guid}/card")]
    public async Task<IActionResult> Card(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Include(p => p.Allergies).SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();
        await AccessLog.RecordAsync(db, await actors.ResolveAsync(User, ct), id, "Patient card", ct);

        var clinic = await db.ClinicSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var allergies = patient.Allergies.OrderBy(a => a.Allergen)
            .Select(a => a.IsNkda ? "NKDA" : a.Allergen).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
        var hasEmergency = !string.IsNullOrWhiteSpace(patient.EmergencyContactName) || !string.IsNullOrWhiteSpace(patient.EmergencyContactNumber);

        return Ok(new
        {
            patient_id = patient.PatientId,
            patient_code = patient.PatientCode,
            full_name = string.Join(" ", new[] { patient.FirstName, patient.MiddleName, patient.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
            birth_date = patient.DateOfBirth.ToString("yyyy-MM-dd"),
            sex = patient.Sex.ToString(),
            blood_type = patient.BloodType,
            allergies,
            emergency_contact = hasEmergency
                ? new { name = patient.EmergencyContactName, phone = patient.EmergencyContactNumber, relationship = patient.EmergencyContactRelationship }
                : null,
            clinic_name = clinic?.ClinicName ?? "",
            clinic_phone = clinic?.ContactNumber,
            issued_at = DateTimeOffset.UtcNow,
        });
    }
    //JJQR - END

    /// <summary>Create a patient record (guest / staff quick-register). No auth
    /// account is linked here — contract §9 keeps that a separate layer.</summary>
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpPost]
    public async Task<ActionResult<Patient>> Create(Patient payload, CancellationToken ct)
    {
        // JJQR - AI (Nov 20, 2026) - B-01 / B-33: refuse blank/oversized names, bad phone/email and a future birth date
        if (Rules.Patient(payload).Result() is { } invalid) return invalid;
        // JJQR - END
        var now = DateTimeOffset.UtcNow;
        payload.PatientId = payload.PatientId == Guid.Empty ? Guid.NewGuid() : payload.PatientId;
        payload.UserId = null;
        payload.CreatedAt = now;
        payload.UpdatedAt = now;

        // §17.1 #1 — patient_code is always issued here (monotonic `MF-000123`); a code sent by
        // the client is ignored. If two writers ever race to the same code the unique index
        // rejects it and we retry with the next one — never a 500 or a half-saved patient.
        var userId = CurrentUserId();
        await PatientCodes.SaveWithFreshCodeAsync(db, codes, code =>
        {
            payload.PatientCode = code;
            db.Patients.Add(payload);
            AuditLogWriter.Add(db, AuditEntityType.Patient, payload.PatientId, "Created", userId,
                $"Code: {code}"); // JJQR - AI (Nov 20, 2026) - PV-9: no patient name in the log // JJQR - END
        }, ct);
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
    public async Task<ActionResult<List<PatientVaccination>>> GetVaccinations(Guid id, CancellationToken ct)
    {
        await AccessLog.RecordAsync(db, await actors.ResolveAsync(User, ct), id, "Vaccinations", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(await db.PatientVaccinations.AsNoTracking().Where(v => v.PatientId == id).OrderByDescending(v => v.AdministeredDate).ToListAsync(ct));
    }

    //JJQR - AI (Oct 31, 2026)
    [HttpGet("{id:guid}/allergies")]
    [Authorize(Roles = "Admin,Staff,Doctor")]
    public async Task<ActionResult<List<PatientAllergy>>> GetAllergies(Guid id, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).CanAccessPatient(id)) return NotFound();
        await AccessLog.RecordAsync(db, await actors.ResolveAsync(User, ct), id, "Allergies", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(await db.PatientAllergies.AsNoTracking()
            .Where(a => a.PatientId == id)
            .OrderBy(a => a.Allergen)
            .ToListAsync(ct));
    }

    //JJQR - AI (Nov 21, 2026) client-supplied row id (offline sync)
    public record PatientAllergyInput(string Allergen, string? Reaction, string? Severity, bool IsNkda, Guid? Id = null);
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    private async Task<Guid> NewAllergyIdAsync(Guid? requested, HashSet<Guid> used, CancellationToken ct)
    {
        var id = await ClientIds.FreshAsync<PatientAllergy>(db, requested, ct);
        if (!used.Add(id)) { id = Guid.NewGuid(); used.Add(id); }
        return id;
    }
    //JJQR - END

    /// <summary>Replace all allergy rows for a patient (including NKDA sentinel rows).</summary>
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpPut("{id:guid}/allergies")]
    public async Task<ActionResult<List<PatientAllergy>>> ReplaceAllergies(Guid id, [FromBody] List<PatientAllergyInput> payload, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).CanAccessPatient(id)) return NotFound();
        //JJQR - AI (Nov 21, 2026) cloud sync: allergies belong to the patient document, so touching them bumps the patient's row_version
        var patientRow = await db.Patients.FirstOrDefaultAsync(p => p.PatientId == id, ct);
        if (patientRow is null) return NotFound();
        patientRow.UpdatedAt = DateTimeOffset.UtcNow;
        //JJQR - END

        var existing = await db.PatientAllergies.Where(a => a.PatientId == id).ToListAsync(ct);
        db.PatientAllergies.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        var rows = new List<PatientAllergy>();
        var usedIds = new HashSet<Guid>(); //JJQR - AI (Nov 21, 2026) client-supplied ids must be unique within the list // JJQR - END
        foreach (var item in payload ?? [])
        {
            if (item.IsNkda)
            {
                rows.Add(new PatientAllergy
                {
                    Id = await NewAllergyIdAsync(item.Id, usedIds, ct), PatientId = id, Allergen = "NKDA", IsNkda = true, //JJQR - AI (Nov 21, 2026) client-supplied id // JJQR - END
                    CreatedAt = now, UpdatedAt = now,
                });
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.Allergen)) continue;
            rows.Add(new PatientAllergy
            {
                Id = await NewAllergyIdAsync(item.Id, usedIds, ct), //JJQR - AI (Nov 21, 2026) client-supplied id // JJQR - END
                PatientId = id,
                Allergen = item.Allergen.Trim(),
                Reaction = string.IsNullOrWhiteSpace(item.Reaction) ? null : item.Reaction.Trim(),
                Severity = string.IsNullOrWhiteSpace(item.Severity) ? null : item.Severity.Trim(),
                IsNkda = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        db.PatientAllergies.AddRange(rows);
        await db.SaveChangesAsync(ct);
        return Ok(await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == id).OrderBy(a => a.Allergen).ToListAsync(ct));
    }

    [HttpGet("{id:guid}/lab-values")]
    [Authorize(Roles = "Admin,Staff,Doctor")]
    public async Task<ActionResult<List<PatientLabValue>>> GetLabValues(Guid id, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).CanAccessPatient(id)) return NotFound();
        await AccessLog.RecordAsync(db, await actors.ResolveAsync(User, ct), id, "Lab values", ct); // JJQR - AI (Nov 20, 2026) - PV-5: access log
        return Ok(await db.PatientLabValues.AsNoTracking()
            .Where(v => v.PatientId == id)
            .OrderByDescending(v => v.ObservedAt)
            .ToListAsync(ct));
    }
    //JJQR - END

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, Patient payload, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).CanAccessPatient(id)) return NotFound();

        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();

        // JJQR - AI (Nov 20, 2026) - B-01 / B-33: same rules as create (email is not editable here, so it is not checked)
        payload.Email = patient.Email;
        if (Rules.Patient(payload).Result() is { } invalid) return invalid;
        // JJQR - END

        var before = new
        {
            patient.FirstName, patient.MiddleName, patient.LastName, patient.DateOfBirth, patient.Sex,
            patient.CivilStatus, patient.Address, patient.City, patient.ZipCode, patient.ContactNumber,
            patient.EmergencyContactName, patient.EmergencyContactNumber, patient.EmergencyContactRelationship,
            patient.BloodType, patient.PhilhealthNumber, patient.HmoProvider, patient.HmoCardNumber
        };

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
        patient.UpdatedAt = DateTimeOffset.UtcNow;

        var details = AuditLogWriter.ChangedFields( // JJQR - AI (Nov 20, 2026) - PV-9: field names only // JJQR - END
            ("First name", before.FirstName, patient.FirstName), ("Middle name", before.MiddleName, patient.MiddleName),
            ("Last name", before.LastName, patient.LastName), ("Date of birth", before.DateOfBirth, patient.DateOfBirth),
            ("Sex", before.Sex, patient.Sex), ("Civil status", before.CivilStatus, patient.CivilStatus),
            ("Address", before.Address, patient.Address), ("City", before.City, patient.City),
            ("Zip code", before.ZipCode, patient.ZipCode), ("Contact number", before.ContactNumber, patient.ContactNumber),
            ("Emergency contact name", before.EmergencyContactName, patient.EmergencyContactName),
            ("Emergency contact number", before.EmergencyContactNumber, patient.EmergencyContactNumber),
            ("Emergency contact relationship", before.EmergencyContactRelationship, patient.EmergencyContactRelationship),
            ("Blood type", before.BloodType, patient.BloodType), ("PhilHealth number", before.PhilhealthNumber, patient.PhilhealthNumber),
            ("HMO provider", before.HmoProvider, patient.HmoProvider), ("HMO card number", before.HmoCardNumber, patient.HmoCardNumber));
        AuditLogWriter.Add(db, AuditEntityType.Patient, patient.PatientId, "Updated", CurrentUserId(), details);

        await db.SaveChangesAsync(ct);
        return Ok(patient);
    }

    /// <summary>Consent acceptance per contract §10 (body = version number).
    // JJQR - AI (Nov 20, 2026) - PV-2/PV-12: only the patient themself can consent for themself (staff could before), the version can't be one the clinic never published,
    // and the decision is also written to the consent history. Declining / withdrawing: POST /api/patients/{id}/consent-decision.</summary>
    [HttpPut("{id:guid}/consent")]
    public async Task<IActionResult> UpdateConsent(Guid id, [FromBody] int consentVersion, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsPatient || actor.PatientId != id) return NotFound();

        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();

        var current = await db.ClinicSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => (int?)s.ConsentVersion).SingleOrDefaultAsync(ct) ?? 1;
        if (consentVersion < 1 || consentVersion > current) return BadRequest(new { message = "That privacy notice version does not exist." });

        var now = DateTimeOffset.UtcNow;
        patient.ConsentVersion = consentVersion;
        patient.ConsentedAt = now;
        patient.ConsentWithdrawnAt = null;
        db.PatientConsents.Add(new PatientConsent { Id = Guid.NewGuid(), PatientId = id, ConsentVersion = consentVersion, Decision = ConsentDecision.Accepted, DecidedAt = now });
        AuditLogWriter.Add(db, AuditEntityType.Patient, id, "ConsentAccepted", actor.UserId, $"Privacy notice v{consentVersion}");
        await db.SaveChangesAsync(ct);
        return Ok(patient);
    }
    // JJQR - END

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
