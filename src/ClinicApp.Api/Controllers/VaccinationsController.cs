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
    VaccinationStatus Status, VaccinationSource Source, string? Notes, string? ReactionNotes);

/// <summary>One dose staged in the consultation's Vaccinations step. Patient /
/// administering user / date come from the consultation, not the payload.</summary>
public record VaccinationInput(
    string VaccineName, string? Manufacturer, short? DoseNumber,
    string? Route, string? Site, string? LotNumber,
    DateOnly? ExpiryDate, DateOnly? NextDoseDate, string? Notes);

/// <summary>patient_vaccinations (contract §4). The list is patient-facing; the
/// write path is the consultation's Vaccinations step — a replace-all set of the
/// doses given at this visit (`Source = AdministeredInClinic`), keyed on
/// consultation_id, same shape as lab orders / diagnoses.</summary>
[ApiController]
[Authorize]
[Route("api/patient-vaccinations")]
public class VaccinationsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PatientVaccination>>> GetAll([FromQuery] Guid? patientId, CancellationToken ct)
    {
        var q = db.PatientVaccinations.AsNoTracking().AsQueryable();
        if (patientId is not null) q = q.Where(v => v.PatientId == patientId);
        return Ok(await q.OrderByDescending(v => v.AdministeredDate).ToListAsync(ct));
    }

    [HttpGet("by-consultation/{consultationId:guid}")]
    public async Task<ActionResult<List<PatientVaccination>>> GetByConsultation(Guid consultationId, CancellationToken ct) =>
        Ok(await db.PatientVaccinations.AsNoTracking()
            .Where(v => v.ConsultationId == consultationId)
            .OrderBy(v => v.CreatedAt).ToListAsync(ct));

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

        var existing = db.PatientVaccinations.Where(v =>
            v.ConsultationId == consultationId && v.Source == VaccinationSource.AdministeredInClinic);
        db.PatientVaccinations.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var uid = CurrentUserId();
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.VaccineName)) continue;
            db.PatientVaccinations.Add(new PatientVaccination
            {
                Id = Guid.NewGuid(),
                PatientId = consult.PatientId,
                ConsultationId = consultationId,
                VaccineName = i.VaccineName.Trim(),
                Manufacturer = string.IsNullOrWhiteSpace(i.Manufacturer) ? null : i.Manufacturer.Trim(),
                DoseNumber = i.DoseNumber,
                Route = string.IsNullOrWhiteSpace(i.Route) ? null : i.Route.Trim(),
                Site = string.IsNullOrWhiteSpace(i.Site) ? null : i.Site.Trim(),
                LotNumber = string.IsNullOrWhiteSpace(i.LotNumber) ? null : i.LotNumber.Trim(),
                ExpiryDate = i.ExpiryDate,
                AdministeredDate = today,
                AdministeredBy = uid,
                NextDoseDate = i.NextDoseDate,
                Status = VaccinationStatus.Administered,
                Source = VaccinationSource.AdministeredInClinic,
                Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim(),
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
        var now = DateTimeOffset.UtcNow;
        var row = new PatientVaccination
        {
            Id = Guid.NewGuid(),
            PatientId = req.PatientId,
            ConsultationId = req.ConsultationId,
            VaccineName = req.VaccineName,
            Manufacturer = req.Manufacturer,
            DoseNumber = req.DoseNumber,
            Route = req.Route,
            Site = req.Site,
            LotNumber = req.LotNumber,
            ExpiryDate = req.ExpiryDate,
            AdministeredDate = req.AdministeredDate,
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
