//JJQR - AI (21 Nov 2026) G5: a node may push doctor_day_status (the front desk / the doctor sets "Available / Running late / Not coming" offline). Sync rule change only, no schema change.
// Allowed actors: Admin, Staff (front desk, same as PUT /api/doctors/{id}/day-status) or the doctor himself. Natural key (doctor_id, status_date); newest edit time wins per field
// (status, running_late_minutes) against the cloud row's updated_at. Deletes are not accepted. Still pulled like before (PullOnly).
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure.Sync;

public sealed partial class SyncPushEngine
{
    private async Task<SyncOperationResult> DoctorDayStatusAsync(Guid nodeId, SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "A doctor's day status is never deleted; set it back to Available.");
        var values = SyncRows.Read(db, typeof(DoctorDayStatus), op.Data);
        if (IdMismatch(values, "id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var byName = values.ToDictionary(kv => SyncRows.Name(kv.Key), kv => kv.Value);
        if (byName.GetValueOrDefault("doctor_id") is not Guid doctorId || doctorId == Guid.Empty)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "doctor_id is required.");
        if (byName.GetValueOrDefault("status_date") is not DateOnly date || date == default)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "status_date is required.");
        if (!await db.Doctors.AnyAsync(d => d.DoctorId == doctorId, ct)) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "Unknown doctor.");

        // actor: Admin, Staff or the doctor himself
        if (op.ActorUserId is not Guid uid || uid == Guid.Empty)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.ActorRequired, "actor_user_id is required for this aggregate.");
        var staff = await db.StaffAccounts.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == uid, ct);
        if (staff is null || staff.Status == StaffStatus.Inactive)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.ActorUnknown, "actor_user_id is not an active staff, doctor or admin user in the cloud.");
        var role = (await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == uid, ct))?.Role
            ?? staff.Role switch { StaffRole.Admin => UserRole.Admin, StaffRole.Doctor => UserRole.Doctor, _ => UserRole.Staff };
        if (role == UserRole.Doctor && staff.StaffId != doctorId)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.ActorNotAllowed, "A doctor may only change his own day status.");
        var actor = new SelfActor(uid, role, staff.StaffId);

        var existing = await db.DoctorDayStatuses.FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.StatusDate == date, ct);
        var status = byName.GetValueOrDefault("status") is DoctorDayStatusEnum st ? st : DoctorDayStatusEnum.Available;
        var late = byName.GetValueOrDefault("running_late_minutes") as int?;
        if (late is < 0 or > 1440) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "running_late_minutes must be 0-1440.");
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            db.DoctorDayStatuses.Add(new DoctorDayStatus
            {
                Id = id, DoctorId = doctorId, StatusDate = date, Status = status, RunningLateMinutes = late,
                CreatedAt = byName.GetValueOrDefault("created_at") is DateTimeOffset c && c != default ? c : now, UpdatedAt = now
            });
            var created = new MergeOutcome();
            created.Applied.Add(("created", null, $"{date:yyyy-MM-dd} {status}"));
            return await FinishSelfEditAsync(nodeId, op, id, doctorId, actor, created, false, "doctor day status set", ct);
        }

        // existing row (maybe under another id): newest edit wins per field
        var cloudAt = existing.UpdatedAt;
        var m = Merge(SyncAggregates.DoctorDayStatus, SyncAggregates.Id(existing.Id),
        [
            new Edit("status", existing.Status, status, () => existing.Status = status, NodeTime(op, "status"), cloudAt, true),
            new Edit("running_late_minutes", existing.RunningLateMinutes, late, () => existing.RunningLateMinutes = late, NodeTime(op, "running_late_minutes"), cloudAt, true),
        ]);
        return await FinishSelfEditAsync(nodeId, op, existing.Id, doctorId, actor, m, existing.Id != id, "doctor day status changed", ct);
    }
}
//JJQR - END
