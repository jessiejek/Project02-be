using ClinicApp.Api.Security;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record SearchPatientHit(Guid Id, string PatientCode, string FirstName, string LastName);
public record SearchDoctorHit(Guid DoctorId, string FullName, string Specialization);
public record SearchBookingHit(Guid BookingId, Guid PatientId, Guid DoctorId, string AppointmentDate, string Status, string? QueueNumber, string PatientName);

public record GlobalSearchResponse(
    IReadOnlyList<SearchPatientHit> Patients,
    IReadOnlyList<SearchDoctorHit> Doctors,
    IReadOnlyList<SearchBookingHit> Bookings);

//JJQR - AI (Oct 31, 2026)
[ApiController]
[Authorize]
[Route("api/search")]
public class SearchController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    private const int Cap = 8;

    [HttpGet]
    public async Task<ActionResult<GlobalSearchResponse>> Search([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new GlobalSearchResponse([], [], []));

        var s = q.Trim();
        var actor = await actors.ResolveAsync(User, ct);

        IReadOnlyList<SearchPatientHit> patients = [];
        IReadOnlyList<SearchDoctorHit> doctors = [];
        IReadOnlyList<SearchBookingHit> bookings = [];

        if (actor.IsStaffLike || actor.IsDoctor)
        {
            patients = await db.Patients.AsNoTracking()
                .Where(p => p.DeletedAt == null && p.MergedIntoPatientId == null) //JJQR - AI (2026-11-26) - GAV-12 merged-away patients are not found //JJQR - END
                .Where(p =>
                    p.FirstName.Contains(s) || p.LastName.Contains(s) ||
                    p.PatientCode.Contains(s) || p.Email.Contains(s) ||
                    (p.ContactNumber != null && p.ContactNumber.Contains(s)))
                .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
                .Take(Cap)
                .Select(p => new SearchPatientHit(p.PatientId, p.PatientCode, p.FirstName, p.LastName))
                .ToListAsync(ct);
        }

        if (actor.IsStaffLike || actor.IsDoctor || actor.IsPatient)
        {
            doctors = await db.Doctors.AsNoTracking()
                .Include(d => d.StaffAccount)
                .Where(d =>
                    d.Specialization.Contains(s) ||
                    (d.StaffAccount != null && d.StaffAccount.FullName.Contains(s)))
                .OrderBy(d => d.StaffAccount!.FullName)
                .Take(Cap)
                .Select(d => new SearchDoctorHit(d.DoctorId, d.StaffAccount!.FullName, d.Specialization))
                .ToListAsync(ct);
        }

        var bookingQuery = db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .AsQueryable();

        if (actor.IsPatient)
        {
            if (actor.PatientId is null) return Forbid();
            bookingQuery = bookingQuery.Where(b => b.PatientId == actor.PatientId);
        }
        else if (actor.IsDoctor)
        {
            if (actor.StaffId is null) return Ok(new GlobalSearchResponse(patients, doctors, []));
            bookingQuery = bookingQuery.Where(b => b.DoctorId == actor.StaffId);
        }
        else if (!actor.IsStaffLike)
        {
            return Forbid();
        }

        bookingQuery = bookingQuery.Where(b =>
            (b.Patient.FirstName + " " + b.Patient.LastName).Contains(s) ||
            b.Patient.PatientCode.Contains(s) ||
            (b.QueueNumber != null && b.QueueNumber.Contains(s)) ||
            b.Status.ToString().Contains(s));

        bookings = await bookingQuery
            .OrderByDescending(b => b.AppointmentDate)
            .Take(Cap)
            .Select(b => new SearchBookingHit(
                b.BookingId,
                b.PatientId,
                b.DoctorId,
                b.AppointmentDate.ToString("yyyy-MM-dd"),
                b.Status.ToString(),
                b.QueueNumber,
                b.Patient.FirstName + " " + b.Patient.LastName))
            .ToListAsync(ct);

        return Ok(new GlobalSearchResponse(patients, doctors, bookings));
    }
}
//JJQR - END
