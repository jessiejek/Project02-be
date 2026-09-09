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

/// <summary>patient_vaccinations (contract §4). List today is patient-facing;
/// write path is for the consultation vaccination step.</summary>
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
