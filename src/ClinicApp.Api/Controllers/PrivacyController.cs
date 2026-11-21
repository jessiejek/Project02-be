//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
// JJQR - AI (Nov 20, 2026) - PV-12/13/14: consent decisions, "download my data", correction/deletion requests (RA 10173 data-subject rights)
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Security;
using ClinicApp.Api.Validation;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record ConsentDecisionRequest(string? Decision);
public record DataRequestCreate(string? Kind, string? Details,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? Id = null
    //JJQR - END
    );
public record DataRequestResolve(string? Status, string? Note);

[ApiController]
[Authorize]
[Route("api")]
public class PrivacyController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    /// <summary>Years the clinic must keep a medical record before it may be deleted.</summary>
    public const int RetentionYears = 7;

    // ── PV-12: consent ────────────────────────────────────────────────────────
    /// <summary>The patient themself accepts, declines or withdraws consent. The version is always the clinic's CURRENT one
    /// (the caller cannot choose it). Each decision adds a dated row; nothing is overwritten.</summary>
    [HttpPost("patients/{id:guid}/consent-decision")]
    public async Task<IActionResult> Decide(Guid id, [FromBody] ConsentDecisionRequest body, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsPatient || actor.PatientId != id) return NotFound();
        var decision = ConsentDecision.All.FirstOrDefault(d => string.Equals(d, body.Decision, StringComparison.OrdinalIgnoreCase));
        if (decision is null) return BadRequest(new { message = "Decision must be Accepted, Declined or Withdrawn." });

        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();
        var version = await db.ClinicSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => (int?)s.ConsentVersion).SingleOrDefaultAsync(ct) ?? 1;
        var now = DateTimeOffset.UtcNow;

        if (decision == ConsentDecision.Withdrawn && patient.ConsentedAt is null)
            return BadRequest(new { message = "There is no active consent to withdraw." });

        switch (decision)
        {
            case ConsentDecision.Accepted:
                patient.ConsentVersion = version;
                patient.ConsentedAt = now;
                patient.ConsentWithdrawnAt = null;
                break;
            case ConsentDecision.Withdrawn:
                patient.ConsentedAt = null;
                patient.ConsentWithdrawnAt = now;
                break;
            // Declined: the earlier state (if any) is left as it was; the decision is only recorded.
        }
        db.PatientConsents.Add(new PatientConsent { Id = Guid.NewGuid(), PatientId = id, ConsentVersion = version, Decision = decision, DecidedAt = now });
        AuditLogWriter.Add(db, AuditEntityType.Patient, id, "Consent" + decision, actor.UserId, $"Privacy notice v{version}");
        await db.SaveChangesAsync(ct);
        return Ok(new { decision, consent_version = version, decided_at = now, consented_at = patient.ConsentedAt, consent_withdrawn_at = patient.ConsentWithdrawnAt });
    }

    /// <summary>The patient's own consent history (newest first). Admins can read it too.</summary>
    [HttpGet("patients/{id:guid}/consents")]
    public async Task<IActionResult> Consents(Guid id, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!(actor.IsAdmin || (actor.IsPatient && actor.PatientId == id))) return NotFound();
        var rows = await db.PatientConsents.AsNoTracking().Where(c => c.PatientId == id)
            .OrderByDescending(c => c.DecidedAt)
            .Select(c => new { c.ConsentVersion, c.Decision, c.DecidedAt }).ToListAsync(ct);
        return Ok(rows);
    }

    // ── PV-13: patient downloads their own data ───────────────────────────────
    /// <summary>Everything the clinic holds about the signed-in patient, as one JSON file. Own data only — no staff can fetch it for someone else.</summary>
    [HttpGet("patients/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsPatient || actor.PatientId != id) return NotFound();

        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == id, ct);
        if (patient is null) return NotFound();

        var package = new Dictionary<string, object?>
        {
            ["exported_at"] = DateTimeOffset.UtcNow,
            ["about"] = "Everything Dr. Grace Gavino Medical Clinic holds about you in its patient system (RA 10173 right to access).",
            ["profile"] = patient,
            ["allergies"] = await db.PatientAllergies.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["bookings"] = await db.Bookings.AsNoTracking().Where(x => x.PatientId == id).OrderBy(x => x.AppointmentDate).ToListAsync(ct),
            ["payments"] = await db.Payments.AsNoTracking().Where(p => db.Bookings.Any(b => b.BookingId == p.BookingId && b.PatientId == id)).ToListAsync(ct),
            ["consultations"] = await db.Consultations.AsNoTracking().Where(x => x.PatientId == id).Include(c => c.ConsultationDiagnoses).OrderBy(x => x.CreatedAt).ToListAsync(ct),
            ["medical_certificates"] = await db.MedicalCertificates.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["prescriptions"] = await db.PrescriptionGroups.AsNoTracking().Where(x => x.PatientId == id)
                .Select(g => new { group = g, items = db.PrescriptionLineItems.Where(i => i.GroupId == g.GroupId).ToList() }).ToListAsync(ct),
            ["vital_readings"] = await db.PatientVitalReadings.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["lab_values"] = await db.PatientLabValues.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["lab_orders"] = await db.LabOrders.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["lab_results"] = await db.PatientLabResults.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["vaccinations"] = await db.PatientVaccinations.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["follow_ups"] = await db.FollowUps.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            ["reviews"] = await db.Reviews.AsNoTracking().Where(x => x.PatientId == id).ToListAsync(ct),
            // File metadata only; the files themselves stay behind the signed-in download endpoint.
            ["documents"] = await db.PatientDocuments.AsNoTracking().Where(x => x.PatientId == id)
                .Select(d => new { d.Id, d.FileName, d.FileSize, d.FileContentType, d.Title, d.Description, d.UploadedAt }).ToListAsync(ct),
            ["consents"] = await db.PatientConsents.AsNoTracking().Where(c => c.PatientId == id).OrderBy(c => c.DecidedAt)
                .Select(c => new { c.ConsentVersion, c.Decision, c.DecidedAt }).ToListAsync(ct),
            ["data_requests"] = await db.DataRequests.AsNoTracking().Where(r => r.PatientId == id).OrderBy(r => r.RequestedAt)
                .Select(r => new { r.Kind, r.Details, r.Status, r.RequestedAt, r.RetainUntil, r.ResolvedAt }).ToListAsync(ct),
        };

        AuditLogWriter.Add(db, AuditEntityType.Patient, id, "DataExported", actor.UserId, "Patient downloaded their own data");
        await db.SaveChangesAsync(ct);

        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentDisposition = $"attachment; filename=\"my-clinic-data-{DateTime.UtcNow:yyyyMMdd}.json\"";
        return new JsonResult(package);
    }

    // ── PV-14: correction / deletion requests ─────────────────────────────────
    private static object Shape(DataRequest r) => new
    {
        r.Id, r.PatientId, r.Kind, r.Details, r.Status, r.RequestedAt, r.RetainUntil, r.ResolvedAt, r.ResolutionNote,
    };

    /// <summary>The patient asks the clinic to correct or delete their data. A deletion of the medical record can't happen before
    /// last visit + 7 years, so the request is logged as Scheduled for that date (or Pending when there is no visit on file).</summary>
    [HttpPost("privacy/data-requests")]
    public async Task<IActionResult> CreateRequest([FromBody] DataRequestCreate body, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsPatient || actor.PatientId is not { } pid) return Forbid();

        var kind = DataRequestKind.All.FirstOrDefault(k => string.Equals(k, body.Kind, StringComparison.OrdinalIgnoreCase));
        var check = new Check();
        if (kind is null) check.Fail("kind", "Choose Correction or Deletion.");
        check.Required("details", "Details", body.Details, 1000);
        if (check.Result() is { } bad) return bad;

        var open = await db.DataRequests.AnyAsync(r => r.PatientId == pid && r.Kind == kind && (r.Status == DataRequestStatus.Pending || r.Status == DataRequestStatus.Scheduled), ct);
        if (open) return Conflict(new { message = $"You already have an open {kind!.ToLowerInvariant()} request. We will update you when it is handled." });

        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (await ClientIds.TakenAsync<DataRequest>(db, body.Id, ct)) return ClientIds.Conflict();
        //JJQR - END
        var req = new DataRequest
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            Id = ClientIds.Use(body.Id),
            //JJQR - END
            PatientId = pid, Kind = kind!, Details = body.Details!.Trim(),
            Status = DataRequestStatus.Pending, RequestedAt = DateTimeOffset.UtcNow,
        };
        if (kind == DataRequestKind.Deletion)
        {
            var lastVisit = await db.Bookings.AsNoTracking().Where(b => b.PatientId == pid).MaxAsync(b => (DateOnly?)b.AppointmentDate, ct);
            if (lastVisit is { } lv)
            {
                req.RetainUntil = lv.AddYears(RetentionYears);
                if (req.RetainUntil > DateOnly.FromDateTime(DateTime.UtcNow)) req.Status = DataRequestStatus.Scheduled;
            }
        }
        db.DataRequests.Add(req);
        // The audit line names the kind and date only — not what the patient wrote.
        AuditLogWriter.Add(db, AuditEntityType.DataRequest, req.Id, "Requested", actor.UserId, $"{req.Kind}; status {req.Status}" + (req.RetainUntil is { } ru ? $"; retain until {ru:yyyy-MM-dd}" : ""));
        await db.SaveChangesAsync(ct);
        return Ok(Shape(req));
    }

    /// <summary>The patient's own requests.</summary>
    [HttpGet("privacy/data-requests/mine")]
    public async Task<IActionResult> MyRequests(CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsPatient || actor.PatientId is not { } pid) return Forbid();
        var rows = await db.DataRequests.AsNoTracking().Where(r => r.PatientId == pid).OrderByDescending(r => r.RequestedAt).ToListAsync(ct);
        return Ok(rows.Select(Shape));
    }

    /// <summary>Admin work queue. Joins the patient's name so the admin knows who asked.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("privacy/data-requests")]
    public async Task<IActionResult> AllRequests([FromQuery] string? status, CancellationToken ct)
    {
        var q = db.DataRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        var rows = await (from r in q
                          join p in db.Patients.AsNoTracking() on r.PatientId equals p.PatientId
                          orderby r.RequestedAt descending
                          select new { r.Id, r.PatientId, patient_name = p.FirstName + " " + p.LastName, p.PatientCode, r.Kind, r.Details, r.Status, r.RequestedAt, r.RetainUntil, r.ResolvedAt, r.ResolutionNote })
            .Take(500).ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Admin closes or reschedules a request. A deletion can't be marked Completed before its retention date.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("privacy/data-requests/{id:guid}")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] DataRequestResolve body, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        var req = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (req is null) return NotFound();
        var status = DataRequestStatus.All.FirstOrDefault(s => string.Equals(s, body.Status, StringComparison.OrdinalIgnoreCase));
        var check = new Check();
        if (status is null) check.Fail("status", "Status must be Pending, Scheduled, Completed or Rejected.");
        check.Optional("note", "Note", body.Note, 1000);
        if (check.Result() is { } bad) return bad;
        if (status == DataRequestStatus.Completed && req.Kind == DataRequestKind.Deletion && req.RetainUntil is { } until && until > DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { message = $"The medical record must be kept until {until:yyyy-MM-dd} (7-year retention). Keep the request Scheduled until then." });

        req.Status = status!;
        req.ResolutionNote = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();
        var done = status is DataRequestStatus.Completed or DataRequestStatus.Rejected;
        req.ResolvedAt = done ? DateTimeOffset.UtcNow : null;
        req.ResolvedByUserId = done ? actor.UserId : null;
        AuditLogWriter.Add(db, AuditEntityType.DataRequest, req.Id, "Resolved", actor.UserId, $"{req.Kind}; now {req.Status}");
        await db.SaveChangesAsync(ct);
        return Ok(Shape(req));
    }
}
// JJQR - END
