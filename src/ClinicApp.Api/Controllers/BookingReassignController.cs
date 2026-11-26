//JJQR - AI (2026-11-26) - GAV-11 why: the front desk checked a different person in under an existing patient (same-ish name). The doctor (or an admin)
// moves ONE visit — the booking and everything recorded for it (consultation, diagnoses, prescription, lab orders, certificate, follow-up, vitals,
// vaccinations given in it, uploaded files / lab results, the payment + OR, a review) — to a NEW patient or to an EXISTING other patient.
// The first patient's details and every other visit stay exactly as they were. Completed and paid visits can be moved too (owner, 26 Nov).
// One transaction: if anything fails, nothing changes. Moved rows get a new row_version, so every desktop receives them on its next pull.
using System.Security.Claims;
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Hubs;
using ClinicApp.Api.Security;
using ClinicApp.Api.Validation;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicApp.Infrastructure.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public sealed record ReassignPatientRequest(
    string? Mode,                       // "new" | "existing"
    Patient? NewPatient,                // mode "new": the person's details (a patient_id sent by the app makes a retry safe)
    Guid? TargetPatientId,              // mode "existing"
    string? Reason,                     // at least 5 characters
    List<Guid>? MoveAllergyIds,         // null = the allergies added during this visit (the default); [] = none
    bool AcknowledgeSameDayVisit = false,
    bool AcknowledgePossibleDuplicate = false);

[ApiController]
[Authorize(Roles = "Admin,Doctor")] // owner 26 Nov: doctor and admin only, not the front desk
[Route("api/bookings")]
public class BookingReassignController(ClinicAppDbContext db, ActorResolver actors, IPatientCodeAllocator codes, IHubContext<ClinicHub> hub) : ControllerBase
{
    private static readonly BookingStatus[] Gone = [BookingStatus.Cancelled, BookingStatus.Expired];

    private async Task<(Booking? Booking, IActionResult? Error)> LoadAsync(Guid bookingId, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking().Include(x => x.Patient).Include(x => x.Payment).SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return (null, NotFound(new { message = "Visit not found." }));
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(b.DoctorId)) return (null, Forbid()); // a doctor only moves her own visits; an admin any
        return (b, null);
    }

    /// <summary>"Added during this visit": created on the visit's day (Manila) and not before the booking existed.</summary>
    private static bool AddedDuringVisit(PatientAllergy a, Booking b) =>
        DateOnly.FromDateTime(a.CreatedAt.ToOffset(ClinicClock.Offset).DateTime) == b.AppointmentDate && a.CreatedAt >= b.CreatedAt.AddMinutes(-1);

    private async Task<List<object>> SameDayVisitsAsync(Guid patientId, Booking b, CancellationToken ct) =>
        (await db.Bookings.AsNoTracking()
            .Where(x => x.PatientId == patientId && x.BookingId != b.BookingId && x.AppointmentDate == b.AppointmentDate)
            .Select(x => new { x.BookingId, x.QueueNumber, x.Status }).ToListAsync(ct))
        .Where(x => !Gone.Contains(x.Status))
        .Select(x => (object)new { booking_id = x.BookingId, queue_number = x.QueueNumber, status = x.Status.ToString() }).ToList();

    private async Task<Dictionary<string, int>> CountsAsync(Booking b, CancellationToken ct)
    {
        var cids = await db.Consultations.AsNoTracking().Where(c => c.BookingId == b.BookingId).Select(c => c.ConsultationId).ToListAsync(ct);
        return new()
        {
            ["consultations"] = cids.Count,
            ["prescriptions"] = await db.PrescriptionGroups.CountAsync(x => x.BookingId == b.BookingId, ct),
            ["lab_orders"] = await db.LabOrders.CountAsync(x => cids.Contains(x.ConsultationId), ct),
            ["medical_certificates"] = await db.MedicalCertificates.CountAsync(x => cids.Contains(x.ConsultationId), ct),
            ["follow_ups"] = await db.FollowUps.CountAsync(x => cids.Contains(x.ConsultationId), ct),
            ["vitals"] = await db.PatientVitalReadings.CountAsync(x => x.BookingId == b.BookingId, ct),
            ["vaccinations"] = await db.PatientVaccinations.CountAsync(x => x.ConsultationId != null && cids.Contains(x.ConsultationId.Value), ct),
            ["documents"] = await db.PatientDocuments.CountAsync(x => x.BookingId == b.BookingId, ct),
            ["lab_results"] = await db.PatientLabResults.CountAsync(x => x.BookingId == b.BookingId, ct),
            ["payments"] = await db.Payments.CountAsync(x => x.BookingId == b.BookingId, ct),
            ["reviews"] = await db.Reviews.CountAsync(x => x.BookingId == b.BookingId, ct),
        };
    }

    /// <summary>What the "Wrong patient?" dialog shows before anything moves: the visit, its allergies (ticked = added during this visit),
    /// and — when a target is chosen — whether that person already has a visit that day (a warning, not a stop).</summary>
    [HttpGet("{bookingId:guid}/reassign-preview")]
    public async Task<IActionResult> Preview(Guid bookingId, [FromQuery(Name = "target")] Guid? targetId, CancellationToken ct)
    {
        var (b, error) = await LoadAsync(bookingId, ct);
        if (error is not null) return error;
        var consult = await db.Consultations.AsNoTracking().Where(c => c.BookingId == bookingId).Select(c => new { c.ConsultationId, c.Status }).FirstOrDefaultAsync(ct);
        var allergies = await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == b!.PatientId).OrderBy(a => a.Allergen).ToListAsync(ct);
        object? target = null;
        if (targetId is Guid tid && tid != Guid.Empty)
        {
            var t = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == tid, ct);
            if (t is not null)
            {
                var same = await SameDayVisitsAsync(t.PatientId, b!, ct);
                target = new
                {
                    patient_id = t.PatientId, patient_code = t.PatientCode, first_name = t.FirstName, last_name = t.LastName, date_of_birth = t.DateOfBirth,
                    unavailable = t.DeletedAt != null || t.PatientId == b!.PatientId, has_visit_that_day = same.Count > 0, visits_that_day = same,
                };
            }
        }
        return Ok(new
        {
            booking_id = b!.BookingId, appointment_date = b.AppointmentDate, queue_number = b.QueueNumber, status = b.Status.ToString(),
            consultation_id = consult?.ConsultationId, consultation_status = consult?.Status.ToString(),
            payment_status = b.Payment?.Status.ToString(), or_number = b.Payment?.OrNumber,
            patient = new { patient_id = b.PatientId, patient_code = b.Patient.PatientCode, first_name = b.Patient.FirstName, last_name = b.Patient.LastName },
            allergies = allergies.Select(a => new { id = a.Id, allergen = a.Allergen, reaction = a.Reaction, severity = a.Severity, is_nkda = a.IsNkda, added_during_visit = AddedDuringVisit(a, b) }),
            counts = await CountsAsync(b, ct),
            target,
        });
    }

    [HttpPost("{bookingId:guid}/reassign-patient")]
    public async Task<IActionResult> Reassign(Guid bookingId, ReassignPatientRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length < 5) return BadRequest(new { message = "Write a reason of at least 5 characters." });
        if (reason.Length > 500) return BadRequest(new { message = "The reason is too long (500 characters at most)." });
        var mode = request.Mode?.Trim().ToLowerInvariant();
        if (mode is not ("new" or "existing")) return BadRequest(new { message = "Choose New patient or Existing patient." });

        var (seen, error) = await LoadAsync(bookingId, ct);
        if (error is not null) return error;
        var b = seen!;
        var fromId = b.PatientId;

        // a retry of a move whose reply was lost: the visit already belongs to the patient this request names
        var namedId = mode == "new" ? request.NewPatient?.PatientId : request.TargetPatientId;
        if (namedId is Guid nid && nid != Guid.Empty && nid == fromId)
            return Ok(new { booking_id = b.BookingId, from_patient_id = (Guid?)null, to_patient_id = nid, patient_code = b.Patient.PatientCode, already = true });

        Patient? target = null;
        if (mode == "existing")
        {
            if (request.TargetPatientId is not Guid tid || tid == Guid.Empty) return BadRequest(new { message = "Choose the patient this visit belongs to." });
            target = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == tid, ct);
            if (target is null) return NotFound(new { message = "That patient was not found." });
            if (target.DeletedAt != null) return Conflict(new { error = "patient_deleted", message = "That patient was merged or removed. Choose another." });
            var same = await SameDayVisitsAsync(tid, b, ct);
            if (same.Count > 0 && !request.AcknowledgeSameDayVisit)
                return Conflict(new { error = "target_has_visit_that_day", message = $"{target.FirstName} {target.LastName} already has a visit on {b.AppointmentDate:MMM d, yyyy}. Move this visit anyway?", visits = same });
        }
        else
        {
            var p = request.NewPatient;
            if (p is null) return BadRequest(new { message = "Enter the new patient's details." });
            p.FirstName = p.FirstName?.Trim() ?? ""; p.LastName = p.LastName?.Trim() ?? ""; p.MiddleName = string.IsNullOrWhiteSpace(p.MiddleName) ? null : p.MiddleName.Trim();
            p.Email = p.Email?.Trim() ?? "";
            if (p.DateOfBirth == default) return BadRequest(new { message = "Enter the birthday." });
            if (Rules.Patient(p).Result() is { } invalid) return invalid;
            if (p.PatientId != Guid.Empty && await db.Patients.AsNoTracking().AnyAsync(x => x.PatientId == p.PatientId, ct))
                return Conflict(new { error = "id_conflict", message = "That patient id is already used. Try again." });
            if (!request.AcknowledgePossibleDuplicate)
            {
                var fk = PersonNameKey.Of(p.FirstName); var lk = PersonNameKey.Of(p.LastName);
                var matches = (await db.Patients.AsNoTracking().Where(x => x.DateOfBirth == p.DateOfBirth && x.DeletedAt == null && x.PatientId != fromId)
                        .Select(x => new { x.PatientId, x.PatientCode, x.FirstName, x.LastName, x.DateOfBirth }).ToListAsync(ct))
                    .Where(x => PersonNameKey.Of(x.FirstName) == fk && PersonNameKey.Of(x.LastName) == lk).ToList();
                if (matches.Count > 0)
                    return Conflict(new
                    {
                        error = "possible_duplicate", message = "A patient with this name and birthday is already on file. Choose Existing patient, or create a new one anyway.",
                        matches = matches.Select(m => new { patient_id = m.PatientId, patient_code = m.PatientCode, first_name = m.FirstName, last_name = m.LastName, date_of_birth = m.DateOfBirth }),
                    });
            }
        }

        var me = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (Guid?)null;
        var before = await CountsAsync(b, ct);
        var sourceAllergies = await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == fromId).ToListAsync(ct);
        var moveIds = (request.MoveAllergyIds ?? sourceAllergies.Where(a => AddedDuringVisit(a, b)).Select(a => a.Id).ToList()).ToHashSet();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // claim the visit: a second move (or any other save) racing this one makes it stop instead of moving twice
            var claimed = await db.Bookings.Where(x => x.BookingId == bookingId && x.PatientId == fromId && x.RowVersion == b.RowVersion)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RowVersion, x => x.RowVersion + 1), ct);
            if (claimed != 1)
            {
                await tx.RollbackAsync(ct);
                return Conflict(new { error = "visit_changed", message = "This visit was just changed by someone else. Close this window and try again." });
            }

            // 1. the person the visit really belongs to
            Guid toId; string toCode;
            if (mode == "new")
            {
                var p = request.NewPatient!;
                var now0 = DateTimeOffset.UtcNow;
                p.PatientId = p.PatientId == Guid.Empty ? Guid.NewGuid() : p.PatientId;
                p.UserId = null; p.MergedIntoPatientId = null; p.DeletedAt = null; p.IsGuest = false; p.IsEmailVerified = false;
                p.ConsentedAt = null; p.ConsentVersion = 0; p.ConsentWithdrawnAt = null; p.Allergies = new List<PatientAllergy>();
                p.CreatedAt = now0; p.UpdatedAt = now0; p.RowVersion = 0; p.OriginNodeId = null;
                toCode = await PatientCodes.SaveWithFreshCodeAsync(db, codes, code =>
                {
                    p.PatientCode = code;
                    db.Patients.Add(p);
                    AuditLogWriter.Add(db, AuditEntityType.Patient, p.PatientId, "Created", me, $"Code: {code}. Made from the wrong-patient fix of booking {bookingId}."); // no names
                }, ct);
                toId = p.PatientId;
                db.ChangeTracker.Clear();
            }
            else { toId = target!.PatientId; toCode = target.PatientCode; }

            // 2. the visit and everything recorded for it (only THIS booking; the patient's other visits are never touched)
            var booking = await db.Bookings.SingleAsync(x => x.BookingId == bookingId, ct);
            booking.RowVersion = b.RowVersion; // the claim above already wrote +1; this save adds exactly that +1
            booking.PatientId = toId;
            var finished = booking.Status == BookingStatus.Completed;
            var cids = await db.Consultations.Where(c => c.BookingId == bookingId).Select(c => c.ConsultationId).ToListAsync(ct);
            var openConsult = !await db.Consultations.AnyAsync(c => c.BookingId == bookingId && c.Status != ConsultationStatus.Draft, ct);
            // a visit still in progress for a NEW person is a new-patient visit; a finished (billed) visit keeps its fee record as printed
            if (!finished && openConsult && mode == "new") booking.VisitType = VisitType.New;
            booking.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var x in await db.Consultations.Where(c => c.BookingId == bookingId).ToListAsync(ct)) { x.PatientId = toId; x.UpdatedAt = DateTimeOffset.UtcNow; }
            foreach (var x in await db.LabOrders.Where(c => cids.Contains(c.ConsultationId)).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.MedicalCertificates.Where(c => cids.Contains(c.ConsultationId)).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.FollowUps.Where(c => cids.Contains(c.ConsultationId)).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.PatientVaccinations.Where(c => c.ConsultationId != null && cids.Contains(c.ConsultationId.Value)).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.PrescriptionGroups.Where(c => c.BookingId == bookingId).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.PatientVitalReadings.Where(c => c.BookingId == bookingId).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.PatientDocuments.Where(c => c.BookingId == bookingId).ToListAsync(ct)) x.PatientId = toId;   // files stay where they are on disk; access follows the row
            foreach (var x in await db.PatientLabResults.Where(c => c.BookingId == bookingId).ToListAsync(ct)) x.PatientId = toId;
            foreach (var x in await db.Reviews.Where(c => c.BookingId == bookingId).ToListAsync(ct)) x.PatientId = toId;
            // the payment / OR row is keyed by booking only, so it moves with the visit as it is

            // 3. allergies recorded during this visit (ticked in the dialog) move too; one the other person already has is not copied twice
            var movedAllergies = 0;
            var toMove = await db.PatientAllergies.Where(a => a.PatientId == fromId && moveIds.Contains(a.Id)).ToListAsync(ct);
            if (toMove.Count > 0)
            {
                var targetRows = await db.PatientAllergies.Where(a => a.PatientId == toId).ToListAsync(ct);
                var have = targetRows.Where(a => !a.IsNkda).Select(a => a.Allergen.Trim().ToLowerInvariant()).ToHashSet();
                var movingReal = toMove.Any(a => !a.IsNkda);
                if (movingReal) db.PatientAllergies.RemoveRange(targetRows.Where(a => a.IsNkda)); // a real allergy replaces "no known allergies"
                foreach (var a in toMove)
                {
                    var dup = a.IsNkda ? (have.Count > 0 || targetRows.Any(t => t.IsNkda)) : have.Contains(a.Allergen.Trim().ToLowerInvariant());
                    if (dup) db.PatientAllergies.Remove(a);
                    else { a.PatientId = toId; a.UpdatedAt = DateTimeOffset.UtcNow; movedAllergies++; if (!a.IsNkda) have.Add(a.Allergen.Trim().ToLowerInvariant()); }
                }
                foreach (var pid in new[] { fromId, toId })
                    if (await db.Patients.SingleOrDefaultAsync(x => x.PatientId == pid, ct) is { } pr) pr.UpdatedAt = DateTimeOffset.UtcNow; // the patient documents changed
            }

            // 4. audit on both patients, the visit and its consultation (ids, codes and counts only; the reason as typed)
            var moved = string.Join(", ", before.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}"));
            var what = $"Booking {bookingId} ({b.AppointmentDate:yyyy-MM-dd}{(b.QueueNumber is null ? "" : ", " + b.QueueNumber)}, {b.Status}) moved from {b.Patient.PatientCode} ({fromId}) to {toCode} ({toId}). "
                       + $"Moved: {(moved.Length == 0 ? "the visit only" : moved)}; allergies {movedAllergies}. Reason: {reason}";
            AuditLogWriter.Add(db, AuditEntityType.Patient, fromId, "Visit moved to another patient", me, what);
            AuditLogWriter.Add(db, AuditEntityType.Patient, toId, "Visit moved in from another patient", me, what);
            AuditLogWriter.Add(db, AuditEntityType.Booking, bookingId, "Wrong patient fixed", me, what);
            foreach (var cid in cids) AuditLogWriter.Add(db, AuditEntityType.Consultation, cid, "Wrong patient fixed", me, what);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await Task.WhenAll(
                hub.Clients.Group("staff").SendAsync("QueueUpdated", new { booking_id = bookingId, doctor_id = booking.DoctorId, status = booking.Status.ToString() }),
                hub.Clients.Group($"doctor:{booking.DoctorId}").SendAsync("QueueUpdated", new { booking_id = bookingId, doctor_id = booking.DoctorId, status = booking.Status.ToString() }));
            return Ok(new
            {
                booking_id = bookingId, from_patient_id = fromId, from_patient_code = b.Patient.PatientCode, to_patient_id = toId, patient_code = toCode,
                queue_number = booking.QueueNumber, appointment_date = booking.AppointmentDate, moved = before, allergies_moved = movedAllergies, already = false,
            });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { error = "reassign_failed", message = "The visit could not be moved, so nothing was changed.", detail = ex.GetBaseException().Message });
        }
    }
}
//JJQR - END
