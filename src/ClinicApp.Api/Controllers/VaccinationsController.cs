//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Validation;
using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record CreateVaccinationRequest(
    Guid PatientId, Guid? ConsultationId, string VaccineName, string? Manufacturer,
    short? DoseNumber, string? Route, string? Site, string? LotNumber,
    DateOnly? ExpiryDate, DateOnly? AdministeredDate, Guid? AdministeredBy, DateOnly? NextDoseDate,
    VaccinationStatus Status, VaccinationSource Source, string? Notes, string? ReactionNotes,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? Id = null
    //JJQR - END
    );

/// <summary>One dose staged in the consultation's Vaccinations step. Patient /
/// administering user / date come from the consultation, not the payload.</summary>
public record VaccinationInput(
    string VaccineName, string? Manufacturer, short? DoseNumber,
    string? Route, string? Site, string? LotNumber,
    DateOnly? ExpiryDate, DateOnly? NextDoseDate, string? Notes,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? Id = null,
    //JJQR - END
    //JJQR - AI (21 Nov 2026) optional, backward-compatible: absent = administered today / status Administered / no reaction notes (as before)
    string? ReactionNotes = null,
    DateOnly? AdministeredDate = null,
    VaccinationStatus? Status = null
    //JJQR - END
    );

/// <summary>patient_vaccinations (contract §4). The list is patient-facing; the
/// write path is the consultation's Vaccinations step — a replace-all set of the
/// doses given at this visit (`Source = AdministeredInClinic`), keyed on
/// consultation_id, same shape as lab orders / diagnoses.</summary>
[ApiController]
[Authorize]
[Route("api/patient-vaccinations")]
public class VaccinationsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PatientVaccination>>> GetAll([FromQuery] Guid? patientId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();

        var q = db.PatientVaccinations.AsNoTracking().AsQueryable();
        if (patientId is not null) q = q.Where(v => v.PatientId == patientId);
        return Ok(await q.OrderByDescending(v => v.AdministeredDate).ToListAsync(ct));
    }

    [HttpGet("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<List<PatientVaccination>>> GetByConsultation(Guid consultationId, CancellationToken ct)
    {
        var owner = await db.Consultations.AsNoTracking().Where(c => c.ConsultationId == consultationId)
            .Select(c => (Guid?)c.PatientId).SingleOrDefaultAsync(ct);
        if (owner is null || !(await actors.ResolveAsync(User, ct)).CanAccessPatient(owner.Value)) return Ok(new List<PatientVaccination>());
        return Ok(await db.PatientVaccinations.AsNoTracking()
            .Where(v => v.ConsultationId == consultationId)
            .OrderBy(v => v.CreatedAt).ToListAsync(ct));
    }

    /// <summary>Replace-all for the doses administered at one consultation. Only
    /// touches this consultation's in-clinic rows — a patient's reported/external
    /// history stays put.</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<List<PatientVaccination>>> ReplaceByConsultation(
        Guid consultationId, List<VaccinationInput> items, CancellationToken ct)
    {
        var consult = await db.Consultations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ConsultationId == consultationId, ct);
        if (consult is null) return NotFound(new { message = "Consultation not found." });
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(consult.DoctorId)) return Forbid();

        // JJQR - AI (Nov 20, 2026) - B-01: a staged dose needs a vaccine name; reject blank / oversized / bad dose numbers
        foreach (var i in items)
            if (Rules.Vaccination(i.VaccineName, i.Manufacturer, i.DoseNumber, i.Route, i.Site, i.LotNumber, i.Notes).Result() is { } invalid) return invalid;
        // JJQR - END
        //JJQR - AI (21 Nov 2026)
        foreach (var i in items)
            if (i.ReactionNotes is { Length: > 2000 }) return BadRequest(new { message = "Reaction notes must be 2,000 characters or fewer." });
        //JJQR - END
        var existing = db.PatientVaccinations.Where(v =>
            v.ConsultationId == consultationId && v.Source == VaccinationSource.AdministeredInClinic);
        db.PatientVaccinations.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        var today = ClinicApp.Domain.ClinicClock.Today; // clinic (Manila) date, not the UTC date
        var uid = CurrentUserId();
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.VaccineName)) continue;
            db.PatientVaccinations.Add(new PatientVaccination
            {
                //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
                Id = await ClientIds.FreshAsync<PatientVaccination>(db, i.Id, ct),
                //JJQR - END
                PatientId = consult.PatientId,
                ConsultationId = consultationId,
                VaccineName = i.VaccineName.Trim(),
                Manufacturer = string.IsNullOrWhiteSpace(i.Manufacturer) ? null : i.Manufacturer.Trim(),
                DoseNumber = i.DoseNumber,
                Route = string.IsNullOrWhiteSpace(i.Route) ? null : i.Route.Trim(),
                Site = string.IsNullOrWhiteSpace(i.Site) ? null : i.Site.Trim(),
                LotNumber = string.IsNullOrWhiteSpace(i.LotNumber) ? null : i.LotNumber.Trim(),
                ExpiryDate = i.ExpiryDate,
                AdministeredDate = i.AdministeredDate ?? today, //JJQR - AI (21 Nov 2026) // JJQR - END
                AdministeredBy = uid,
                NextDoseDate = i.NextDoseDate,
                Status = i.Status ?? VaccinationStatus.Administered, //JJQR - AI (21 Nov 2026) // JJQR - END
                Source = VaccinationSource.AdministeredInClinic,
                Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim(),
                ReactionNotes = string.IsNullOrWhiteSpace(i.ReactionNotes) ? null : i.ReactionNotes.Trim(), //JJQR - AI (21 Nov 2026) // JJQR - END
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(await db.PatientVaccinations.AsNoTracking()
            .Where(v => v.ConsultationId == consultationId)
            .OrderBy(v => v.CreatedAt).ToListAsync(ct));
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var gid) ? gid : null;
    }

    [Authorize(Roles = "Doctor,Staff,Admin")]
    [HttpPost]
    public async Task<ActionResult<PatientVaccination>> Create(CreateVaccinationRequest req, CancellationToken ct)
    {
        // JJQR - AI (Nov 20, 2026) - B-01
        if (Rules.Vaccination(req.VaccineName, req.Manufacturer, req.DoseNumber, req.Route, req.Site, req.LotNumber, req.Notes).Result() is { } invalid) return invalid;
        // JJQR - END
        //JJQR - AI (21 Nov 2026) reaction_notes cap, same as PUT by-consultation
        if (req.ReactionNotes is { Length: > 2000 }) return BadRequest(new { message = "Reaction notes must be 2,000 characters or fewer." });
        //JJQR - END
        var now = DateTimeOffset.UtcNow;
        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (await ClientIds.TakenAsync<PatientVaccination>(db, req.Id, ct)) return ClientIds.Conflict();
        //JJQR - END
        var row = new PatientVaccination
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            Id = ClientIds.Use(req.Id),
            //JJQR - END
            PatientId = req.PatientId,
            ConsultationId = req.ConsultationId,
            VaccineName = req.VaccineName,
            Manufacturer = req.Manufacturer,
            DoseNumber = req.DoseNumber,
            Route = req.Route,
            Site = req.Site,
            LotNumber = req.LotNumber,
            ExpiryDate = req.ExpiryDate,
            AdministeredDate = req.AdministeredDate ?? (req.Status == VaccinationStatus.Administered ? ClinicApp.Domain.ClinicClock.Today : null), //JJQR - AI (21 Nov 2026) default today when Administered // JJQR - END
            AdministeredBy = req.AdministeredBy,
            NextDoseDate = req.NextDoseDate,
            Status = req.Status,
            Source = req.Source,
            Notes = req.Notes,
            ReactionNotes = req.ReactionNotes,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PatientVaccinations.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }
}
