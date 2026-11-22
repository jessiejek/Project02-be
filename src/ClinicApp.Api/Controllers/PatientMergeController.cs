//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.4 (D4): the admin merges two patient records BY HAND. Nothing here runs by itself.
// The patient to KEEP stays as it is (empty fields are filled from the other record, nothing is overwritten); every record of the other patient moves to it; the other patient is marked merged
// (soft-deleted + merged_into_patient_id). One transaction: if anything fails, nothing changes. Moved rows get a new row_version, so every desktop receives them on its next pull.
using System.Reflection;
using System.Security.Claims;
using ClinicApp.Api.Auditing;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/patients")]
public class PatientMergeController(ClinicAppDbContext db) : ControllerBase
{
    // never copied from the other record: identity, keys, sync columns, and the privacy consent state (a consent belongs to the person who gave it)
    private static readonly HashSet<string> NeverFilled = new(StringComparer.Ordinal)
    {
        nameof(Patient.PatientId), nameof(Patient.UserId), nameof(Patient.PatientCode), nameof(Patient.MergedIntoPatientId), nameof(Patient.DeletedAt), nameof(Patient.OriginNodeId),
        nameof(Patient.RowVersion), nameof(Patient.CreatedAt), nameof(Patient.UpdatedAt), nameof(Patient.ConsentedAt), nameof(Patient.ConsentVersion), nameof(Patient.ConsentWithdrawnAt),
        nameof(Patient.Allergies), nameof(Patient.IsGuest), nameof(Patient.IsEmailVerified),
    };

    public sealed record MergeRequest(Guid KeepPatientId, Guid MergePatientId, string? Note);

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
    public async Task<IActionResult> Preview([FromQuery(Name = "keep")] Guid keepId, [FromQuery(Name = "merge")] Guid mergeId, CancellationToken ct)
    {
        var (keep, merge, error) = await LoadPairAsync(keepId, mergeId, ct);
        if (error is not null) return error;
        return Ok(new
        {
            keep = Card(keep!, await CountsAsync(keep!.PatientId, ct)),
            merge = Card(merge!, await CountsAsync(merge!.PatientId, ct)),
            both_have_accounts = keep.UserId != null && merge.UserId != null,
        });
    }

    [HttpPost("merge")]
    public async Task<IActionResult> Merge(MergeRequest request, CancellationToken ct)
    {
        var note = request.Note?.Trim() ?? "";
        if (note.Length < 5) return BadRequest(new { message = "Write a note of at least 5 characters." });
        if (note.Length > 1000) return BadRequest(new { message = "The note is too long (1000 characters at most)." });
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
            var haveAllergens = (await db.PatientAllergies.Where(a => a.PatientId == keepId).Select(a => a.Allergen).ToListAsync(ct)).Select(a => a.Trim().ToLowerInvariant()).ToHashSet();
            foreach (var a in await db.PatientAllergies.Where(a => a.PatientId == mergeId).ToListAsync(ct))
            {
                if (haveAllergens.Contains(a.Allergen.Trim().ToLowerInvariant())) db.PatientAllergies.Remove(a);
                else a.PatientId = keepId;
            }

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
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Ok(new { kept_patient_id = keepId, merged_patient_id = mergeId, moved = before, filled_fields = filled });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            return Conflict(new { error = "merge_failed", message = "The merge could not be completed, so nothing was changed.", detail = ex.GetBaseException().Message });
        }
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
//JJQR - END
