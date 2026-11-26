//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.4 (D4): the admin merges two patient records BY HAND. Nothing here runs by itself.
// The patient to KEEP stays as it is (empty fields are filled from the other record, nothing is overwritten); every record of the other patient moves to it; the other patient is marked merged
// (soft-deleted + merged_into_patient_id). One transaction: if anything fails, nothing changes. Moved rows get a new row_version, so every desktop receives them on its next pull.
using System.Reflection;
using System.Security.Claims;
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Hubs; //JJQR - AI (2026-11-26) - GAV-12 why: screens showing a moved visit refresh //JJQR - END
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR; //JJQR - AI (2026-11-26) - GAV-12 why: IHubContext for the QueueUpdated broadcast after a merge //JJQR - END
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
//JJQR - AI (2026-11-26) - GAV-12 review 3.1 why: class-level and action-level role lists are combined as AND, so a class "Admin,Doctor" made Staff get 403 on
// GET merged-into. The class now only needs a signed-in user; EVERY action names its own roles (merge + preview: Admin,Doctor; unmerge: Admin; merged-into: Admin,Staff,Doctor).
[Authorize]
//JJQR - END
[Route("api/patients")]
//JJQR - AI (2026-11-26) - GAV-12 why: + hub (today's moved visits refresh the queue screens)
public class PatientMergeController(ClinicAppDbContext db, IHubContext<ClinicHub> hub) : ControllerBase
//JJQR - END
{
    // never copied from the other record: identity, keys, sync columns, and the privacy consent state (a consent belongs to the person who gave it)
    private static readonly HashSet<string> NeverFilled = new(StringComparer.Ordinal)
    {
        nameof(Patient.PatientId), nameof(Patient.UserId), nameof(Patient.PatientCode), nameof(Patient.MergedIntoPatientId), nameof(Patient.DeletedAt), nameof(Patient.OriginNodeId),
        nameof(Patient.RowVersion), nameof(Patient.CreatedAt), nameof(Patient.UpdatedAt), nameof(Patient.ConsentedAt), nameof(Patient.ConsentVersion), nameof(Patient.ConsentWithdrawnAt),
        nameof(Patient.Allergies), nameof(Patient.IsGuest), nameof(Patient.IsEmailVerified),
    };

    public sealed record MergeRequest(Guid KeepPatientId, Guid MergePatientId, string? Note);
    //JJQR - AI (2026-11-26) - GAV-12 why: light undo for an admin: the merged-away record comes back; its visits stay on the kept patient (move them back one by one with "Wrong patient?")
    public sealed record UnmergeRequest(string? Note);
    private static readonly BookingStatus[] Active = [BookingStatus.CheckedIn, BookingStatus.InProgress, BookingStatus.OnHold];

    /// <summary>Visits of this patient that are open today (checked in, being consulted or on hold): merging is allowed, the screen warns first.</summary>
    private async Task<List<object>> ActiveTodayAsync(Guid id, CancellationToken ct)
    {
        var today = ClinicClock.Today;
        return (await db.Bookings.AsNoTracking().Where(b => b.PatientId == id && b.AppointmentDate == today).Select(b => new { b.BookingId, b.QueueNumber, b.Status }).ToListAsync(ct))
            .Where(b => Active.Contains(b.Status))
            .Select(b => (object)new { booking_id = b.BookingId, queue_number = b.QueueNumber, status = b.Status.ToString() }).ToList();
    }
    //JJQR - END

    private async Task<Dictionary<string, int>> CountsAsync(Guid id, CancellationToken ct) => new()
    {
        ["bookings"] = await db.Bookings.CountAsync(x => x.PatientId == id, ct),
        ["consultations"] = await db.Consultations.CountAsync(x => x.PatientId == id, ct),
        ["medical_certificates"] = await db.MedicalCertificates.CountAsync(x => x.PatientId == id, ct),
        ["vitals"] = await db.PatientVitalReadings.CountAsync(x => x.PatientId == id, ct),
        ["follow_ups"] = await db.FollowUps.CountAsync(x => x.PatientId == id, ct),
        ["lab_orders"] = await db.LabOrders.CountAsync(x => x.PatientId == id, ct),
        ["lab_values"] = await db.PatientLabValues.CountAsync(x => x.PatientId == id, ct),
        ["lab_results"] = await db.PatientLabResults.CountAsync(x => x.PatientId == id, ct),
        ["documents"] = await db.PatientDocuments.CountAsync(x => x.PatientId == id, ct),
        ["vaccinations"] = await db.PatientVaccinations.CountAsync(x => x.PatientId == id, ct),
        ["prescriptions"] = await db.PrescriptionGroups.CountAsync(x => x.PatientId == id, ct),
        ["consents"] = await db.PatientConsents.CountAsync(x => x.PatientId == id, ct),
        ["data_requests"] = await db.DataRequests.CountAsync(x => x.PatientId == id, ct),
        ["reviews"] = await db.Reviews.CountAsync(x => x.PatientId == id, ct),
        ["allergies"] = await db.PatientAllergies.CountAsync(x => x.PatientId == id, ct),
    };

    private static object Card(Patient p, Dictionary<string, int> counts) => new
    {
        patient_id = p.PatientId, patient_code = p.PatientCode, first_name = p.FirstName, middle_name = p.MiddleName, last_name = p.LastName,
        date_of_birth = p.DateOfBirth, sex = p.Sex.ToString(), contact_number = p.ContactNumber, email = p.Email, address = p.Address, city = p.City,
        has_account = p.UserId != null, created_at = p.CreatedAt, counts,
        civil_status = p.CivilStatus, blood_type = p.BloodType, philhealth_number = p.PhilhealthNumber, hmo_provider = p.HmoProvider, //JJQR - AI (2026-11-26) - GAV-12 more rows in the side-by-side compare //JJQR - END
    };

    private async Task<(Patient? Keep, Patient? Merge, IActionResult? Error)> LoadPairAsync(Guid keepId, Guid mergeId, CancellationToken ct)
    {
        if (keepId == Guid.Empty || mergeId == Guid.Empty || keepId == mergeId) return (null, null, BadRequest(new { message = "Choose two different patients." }));
        var keep = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == keepId, ct);
        var merge = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == mergeId, ct);
        if (keep is null || merge is null) return (null, null, NotFound(new { message = "One of the patients was not found." }));
        if (keep.DeletedAt != null || merge.DeletedAt != null)
            return (null, null, Conflict(new { error = "patient_deleted", message = "One of these patients was already merged or removed." }));
        return (keep, merge, null);
    }

    /// <summary>The two records side by side, with what each holds, so the admin can choose which to keep.</summary>
    [HttpGet("merge-preview")]
    [Authorize(Roles = "Admin,Doctor")] //JJQR - AI (2026-11-26) - GAV-12 review 3.1: owner 26 Nov: the DOCTOR merges too; the front desk does not //JJQR - END
    public async Task<IActionResult> Preview([FromQuery(Name = "keep")] Guid keepId, [FromQuery(Name = "merge")] Guid mergeId, CancellationToken ct)
    {
        var (keep, merge, error) = await LoadPairAsync(keepId, mergeId, ct);
        if (error is not null) return error;
        return Ok(new
        {
            keep = Card(keep!, await CountsAsync(keep!.PatientId, ct)),
            merge = Card(merge!, await CountsAsync(merge!.PatientId, ct)),
            both_have_accounts = keep.UserId != null && merge.UserId != null,
            //JJQR - AI (2026-11-26) - GAV-12 why: warn (never block) when either has a visit open today; that visit moves to the kept patient
            keep_active_today = await ActiveTodayAsync(keep.PatientId, ct),
            merge_active_today = await ActiveTodayAsync(merge.PatientId, ct),
            //JJQR - END
        });
    }

    [HttpPost("merge")]
    [Authorize(Roles = "Admin,Doctor")] //JJQR - AI (2026-11-26) - GAV-12 review 3.1: owner 26 Nov: the DOCTOR merges too; the front desk does not //JJQR - END
    public async Task<IActionResult> Merge(MergeRequest request, CancellationToken ct)
    {
        var note = request.Note?.Trim() ?? "";
        if (note.Length < 5) return BadRequest(new { message = "Write a note of at least 5 characters." });
        if (note.Length > 1000) return BadRequest(new { message = "The note is too long (1000 characters at most)." });
        //JJQR - AI (2026-11-26) - GAV-12 review 1.1 why: a resend after a lost reply (the merge already went through) answers OK with already=true, not
        // "already merged or removed", so the doctor never sees an error for a merge that worked. Nothing is changed a second time.
        if (request.KeepPatientId != Guid.Empty && request.MergePatientId != Guid.Empty && request.KeepPatientId != request.MergePatientId)
        {
            var gone = await db.Patients.AsNoTracking().Where(p => p.PatientId == request.MergePatientId)
                .Select(p => new { p.PatientCode, p.MergedIntoPatientId, p.DeletedAt }).FirstOrDefaultAsync(ct);
            if (gone is { MergedIntoPatientId: Guid into, DeletedAt: not null }
                && (into == request.KeepPatientId || await PatientMerges.KeeperOfAsync(db, request.MergePatientId, ct) == request.KeepPatientId))
            {
                var keptCode = await db.Patients.AsNoTracking().Where(p => p.PatientId == request.KeepPatientId).Select(p => p.PatientCode).FirstOrDefaultAsync(ct);
                return Ok(new { kept_patient_id = request.KeepPatientId, kept_patient_code = keptCode, merged_patient_id = request.MergePatientId, merged_patient_code = gone.PatientCode,
                    moved = new Dictionary<string, int>(), filled_fields = Array.Empty<string>(), already = true });
            }
        }
        //JJQR - END
        var (keep, merge, error) = await LoadPairAsync(request.KeepPatientId, request.MergePatientId, ct);
        if (error is not null) return error;
        if (keep!.UserId != null && merge!.UserId != null)
            return Conflict(new { error = "both_have_accounts", message = "Both patients have a patient-portal account, so they cannot be merged here." });

        var keepId = keep.PatientId; var mergeId = merge!.PatientId;
        var before = await CountsAsync(mergeId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. empty fields on the kept record are filled from the other one; nothing is overwritten
            var filled = new List<string>();
            foreach (var prop in typeof(Patient).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite || NeverFilled.Contains(prop.Name)) continue;
                var t = prop.PropertyType;
                var isText = t == typeof(string);
                var isNullable = Nullable.GetUnderlyingType(t) != null;
                if (!isText && !isNullable) continue;
                var mine = prop.GetValue(keep); var theirs = prop.GetValue(merge);
                var mineEmpty = mine is null || (mine is string ms && string.IsNullOrWhiteSpace(ms));
                var theirsEmpty = theirs is null || (theirs is string ts && string.IsNullOrWhiteSpace(ts));
                if (mineEmpty && !theirsEmpty) { prop.SetValue(keep, theirs); filled.Add(prop.Name); }
            }
            // a portal account follows the person: only one of the two has one (checked above)
            if (keep.UserId == null && merge.UserId != null) { keep.UserId = merge.UserId; merge.UserId = null; filled.Add(nameof(Patient.UserId)); }
            keep.UpdatedAt = DateTimeOffset.UtcNow;

            // 2. every record of the other patient moves to the kept one
            foreach (var x in await db.Bookings.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.Consultations.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.MedicalCertificates.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PatientVitalReadings.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.FollowUps.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.LabOrders.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PatientLabValues.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PatientLabResults.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PatientDocuments.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PatientVaccinations.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PrescriptionGroups.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.PatientConsents.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.DataRequests.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            foreach (var x in await db.Reviews.Where(x => x.PatientId == mergeId).ToListAsync(ct)) x.PatientId = keepId;
            // allergies: the union (an allergen the kept patient already has is not copied twice)
            //JJQR - AI (2026-11-26) - GAV-12 why: "no known allergies" (NKDA) is not an allergen: a real allergy replaces it, and it is never kept twice (same rule as GAV-11)
            var keepRows = await db.PatientAllergies.Where(a => a.PatientId == keepId).ToListAsync(ct);
            var mergeRows = await db.PatientAllergies.Where(a => a.PatientId == mergeId).ToListAsync(ct);
            var haveAllergens = keepRows.Where(a => !a.IsNkda).Select(a => a.Allergen.Trim().ToLowerInvariant()).ToHashSet();
            if (mergeRows.Any(a => !a.IsNkda)) { db.PatientAllergies.RemoveRange(keepRows.Where(a => a.IsNkda)); keepRows.RemoveAll(a => a.IsNkda); }
            foreach (var a in mergeRows)
            {
                var dup = a.IsNkda ? (haveAllergens.Count > 0 || keepRows.Any(k => k.IsNkda) || mergeRows.Any(m => !m.IsNkda)) : haveAllergens.Contains(a.Allergen.Trim().ToLowerInvariant());
                if (dup) db.PatientAllergies.Remove(a);
                else { a.PatientId = keepId; a.UpdatedAt = DateTimeOffset.UtcNow; if (!a.IsNkda) haveAllergens.Add(a.Allergen.Trim().ToLowerInvariant()); else keepRows.Add(a); }
            }
            // an earlier merge INTO the merged-away patient now points at the kept one (old links still land on the right chart)
            foreach (var older in await db.Patients.Where(x => x.MergedIntoPatientId == mergeId).ToListAsync(ct)) { older.MergedIntoPatientId = keepId; older.UpdatedAt = DateTimeOffset.UtcNow; }
            //JJQR - END

            // 3. the other patient is marked merged (and soft-deleted, so desktops drop it from their lists)
            merge.MergedIntoPatientId = keepId;
            merge.DeletedAt = DateTimeOffset.UtcNow;
            merge.UpdatedAt = DateTimeOffset.UtcNow;

            // 4. the open "possible duplicate" items about this pair are resolved as merged
            var me = CurrentUserId();
            var now = DateTimeOffset.UtcNow;
            var pair = new[] { keepId, mergeId };
            foreach (var item in await db.SyncReviewItems.Where(i => i.Status == ReviewItemStatus.Open && i.Kind == ReviewItemKinds.PossibleDuplicatePatient
                         && i.PatientId != null && i.OtherPatientId != null && pair.Contains(i.PatientId.Value) && pair.Contains(i.OtherPatientId.Value)).ToListAsync(ct))
            {
                item.Status = ReviewItemStatus.Resolved; item.Resolution = ReviewResolutions.Merged; item.ResolutionNote = note; item.ResolvedAt = now; item.ResolvedByUserId = me;
            }

            // 5. one audit row (ids, codes and counts only, no names)
            AuditLogWriter.Add(db, AuditEntityType.Patient, keepId, "Patients merged", me,
                $"Merged patient {merge.PatientCode} ({mergeId}) into {keep.PatientCode} ({keepId}). Moved: " + (before.Any(kv => kv.Value > 0) ? string.Join(", ", before.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}")) : "no records")
                + (filled.Count > 0 ? $". Filled empty fields: {string.Join(", ", filled)}" : "") + ". Note: " + note);
            //JJQR - AI (2026-11-26) - GAV-12: the merged-away record gets its own audit row too (its old link shows "Merged into ...")
            AuditLogWriter.Add(db, AuditEntityType.Patient, mergeId, "Merged into another patient", me, $"Merged into {keep.PatientCode} ({keepId}). Note: " + note);
            var movedToday = await db.Bookings.AsNoTracking().Where(b => b.PatientId == mergeId && b.AppointmentDate == ClinicClock.Today).Select(b => new { b.BookingId, b.DoctorId, b.Status }).ToListAsync(ct);
            //JJQR - END
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            //JJQR - AI (2026-11-26) - GAV-12 why: today's queue shows the kept patient's name at once; a failed broadcast after the commit is not an error
            try
            {
                foreach (var b in movedToday)
                    await Task.WhenAll(
                        hub.Clients.Group("staff").SendAsync("QueueUpdated", new { booking_id = b.BookingId, doctor_id = b.DoctorId, status = b.Status.ToString() }, ct),
                        hub.Clients.Group($"doctor:{b.DoctorId}").SendAsync("QueueUpdated", new { booking_id = b.BookingId, doctor_id = b.DoctorId, status = b.Status.ToString() }, ct));
            }
            catch (Exception) { /* broadcast only */ }
            return Ok(new { kept_patient_id = keepId, kept_patient_code = keep.PatientCode, merged_patient_id = mergeId, merged_patient_code = merge.PatientCode, moved = before, filled_fields = filled });
            //JJQR - END
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            _ = ex; //JJQR - AI (2026-11-26) - GAV-12: no database text to the client (same as GAV-11) //JJQR - END
            return Conflict(new { error = "merge_failed", message = "The merge could not be completed, so nothing was changed." });
        }
    }

    //JJQR - AI (2026-11-26) - GAV-12 why: light Unmerge (admin only). The merged-away record is live again (searchable, editable) and every desktop gets it back on its next pull.
    // Nothing moves back by itself: the visits stay on the kept patient; the admin moves the wrong ones back with "Wrong patient?". Allergies and filled fields stay as they are.
    [HttpPost("{id:guid}/unmerge")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Unmerge(Guid id, UnmergeRequest request, CancellationToken ct)
    {
        var note = request.Note?.Trim() ?? "";
        if (note.Length < 5) return BadRequest(new { message = "Write a note of at least 5 characters." });
        if (note.Length > 1000) return BadRequest(new { message = "The note is too long (1000 characters at most)." });
        var p = await db.Patients.SingleOrDefaultAsync(x => x.PatientId == id, ct);
        if (p is null) return NotFound(new { message = "Patient not found." });
        if (p.MergedIntoPatientId is not Guid keptId) return Conflict(new { error = "not_merged", message = "This patient was not merged into another record." });
        var keptCode = await db.Patients.AsNoTracking().Where(x => x.PatientId == keptId).Select(x => x.PatientCode).FirstOrDefaultAsync(ct) ?? "?";
        p.MergedIntoPatientId = null;
        p.DeletedAt = null;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        var me = CurrentUserId();
        AuditLogWriter.Add(db, AuditEntityType.Patient, id, "Patient unmerged", me, $"Unmerged {p.PatientCode} ({id}) from {keptCode} ({keptId}); its visits stay on {keptCode}. Note: " + note);
        AuditLogWriter.Add(db, AuditEntityType.Patient, keptId, "Patient unmerged", me, $"{p.PatientCode} ({id}) was unmerged from this record; visits stay here. Note: " + note);
        await db.SaveChangesAsync(ct);
        return Ok(new { patient_id = id, patient_code = p.PatientCode, was_merged_into = keptId, was_merged_into_code = keptCode });
    }

    /// <summary>For a retired chart: the record it was merged into (the end of the chain) so old links can redirect.</summary>
    [HttpGet("{id:guid}/merged-into")]
    [Authorize(Roles = "Admin,Staff,Doctor")]
    public async Task<IActionResult> MergedInto(Guid id, CancellationToken ct)
    {
        var p = await db.Patients.AsNoTracking().Where(x => x.PatientId == id).Select(x => new { x.PatientId, x.PatientCode, x.MergedIntoPatientId }).FirstOrDefaultAsync(ct);
        if (p is null) return NotFound();
        if (p.MergedIntoPatientId is null) return Ok(new { patient_id = id, patient_code = p.PatientCode, merged_into_patient_id = (Guid?)null });
        var keptId = await PatientMerges.KeeperOfAsync(db, id, ct) ?? p.MergedIntoPatientId.Value;
        var kept = await db.Patients.AsNoTracking().Where(x => x.PatientId == keptId).Select(x => new { x.PatientCode, x.FirstName, x.LastName }).FirstOrDefaultAsync(ct);
        return Ok(new { patient_id = id, patient_code = p.PatientCode, merged_into_patient_id = keptId, merged_into_patient_code = kept?.PatientCode, merged_into_name = kept is null ? null : $"{kept.FirstName} {kept.LastName}" });
    }
    //JJQR - END

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
//JJQR - END
