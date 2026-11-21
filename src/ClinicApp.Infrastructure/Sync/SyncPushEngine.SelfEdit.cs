//JJQR - AI (Nov 21, 2026) offline self-edits: a node may push doctor / doctor_schedule / doctor_blocked_date when the op's actor is that doctor
// (or an admin). Field-level merge, newest edit time wins per field; restricted (admin-only) fields stay cloud-owned. Contract §5.7.
using System.Globalization;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure.Sync;

public sealed partial class SyncPushEngine
{
    /// <summary>Fields a doctor may change on his own doctor row (the rest of the row is Admin-only on the web).</summary>
    public static readonly IReadOnlySet<string> DoctorSelfFields = new HashSet<string> { "specialization", "bio", "staff.full_name", "staff.contact_number" };
    /// <summary>Everything an Admin may change through sync on a doctor.</summary>
    public static readonly IReadOnlySet<string> DoctorAdminFields = new HashSet<string>
    {
        "specialization", "bio", "consultation_fee", "license_number", "ptr_number", "s2_number", "slot_duration_minutes", "slot_capacity", "daily_patient_limit",
        "staff.full_name", "staff.contact_number"
    };

    private sealed record SelfActor(Guid UserId, UserRole Role, Guid StaffId)
    {
        public bool IsAdmin => Role == UserRole.Admin;
    }

    // ── actor ────────────────────────────────────────────────────────────
    private async Task<(SelfActor? Actor, SyncOperationResult? Fail)> ResolveSelfActorAsync(SyncPushOperation op, Guid doctorId, CancellationToken ct)
    {
        SyncOperationResult Reject(string reason, string message, Guid? by)
        {
            Audit(doctorId, by, "Sync: self-edit rejected", new { source = "sync", op.OperationId, op.Aggregate, op.EntityId, reason, actor_user_id = op.ActorUserId });
            return Fail(op, SyncStatuses.Rejected, reason, message);
        }
        if (op.ActorUserId is not Guid uid || uid == Guid.Empty)
            return (null, Reject(SyncReasons.ActorRequired, "actor_user_id is required for this aggregate.", null));
        var staff = await db.StaffAccounts.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == uid, ct);
        if (staff is null || !await db.Users.AnyAsync(u => u.Id == uid, ct))
            return (null, Reject(SyncReasons.ActorUnknown, "actor_user_id is not a staff, doctor or admin user in the cloud.", null));
        if (staff.Status == StaffStatus.Inactive)
            return (null, Reject(SyncReasons.ActorUnknown, "The acting user is deactivated.", uid));
        var role = (await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == uid, ct))?.Role
            ?? staff.Role switch { StaffRole.Admin => UserRole.Admin, StaffRole.Doctor => UserRole.Doctor, _ => UserRole.Staff };
        var actor = new SelfActor(uid, role, staff.StaffId);
        if (actor.IsAdmin) return (actor, null);
        if (role == UserRole.Doctor && staff.StaffId == doctorId) return (actor, null);
        return (null, Reject(SyncReasons.ActorNotAllowed,
            role == UserRole.Doctor ? "A doctor may only change his own availability and profile." : "Only the doctor himself or an admin may change this.", uid));
    }

    // ── time helpers ─────────────────────────────────────────────────────
    private static DateTimeOffset NodeTime(SyncPushOperation op, string field)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? t = null;
        static DateTimeOffset? Parse(JsonElement e) =>
            e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p) ? p : null;
        if (op.Payload.ValueKind == JsonValueKind.Object && op.Payload.TryGetProperty("field_updated_at", out var map) && map.ValueKind == JsonValueKind.Object
            && map.TryGetProperty(field, out var f)) t = Parse(f);
        if (t is null && op.Data.ValueKind == JsonValueKind.Object && op.Data.TryGetProperty("updated_at", out var u)) t = Parse(u);
        t ??= op.CreatedAt ?? now;
        return t > now.AddMinutes(5) ? now : t.Value; // a wrong node clock can never win by being in the future
    }

    private async Task<Dictionary<string, DateTimeOffset>> StampsAsync(string aggregate, string entityId, CancellationToken ct) =>
        await db.SyncFieldStamps.AsNoTracking().Where(s => s.Aggregate == aggregate && s.EntityId == entityId).ToDictionaryAsync(s => s.Field, s => s.StampedAt, ct);

    // ── generic field merge ──────────────────────────────────────────────
    private sealed record Edit(string Field, object? Cloud, object? Incoming, Action Apply, DateTimeOffset NodeAt, DateTimeOffset CloudAt, bool Allowed);

    private sealed class MergeOutcome
    {
        public List<(string Field, object? Old, object? New)> Applied { get; } = [];
        public List<string> CloudNewer { get; } = [];
        public List<string> NotAllowed { get; } = [];
        public int Differing => Applied.Count + CloudNewer.Count + NotAllowed.Count;
        public List<string> Kept => [.. NotAllowed, .. CloudNewer];
    }

    private MergeOutcome Merge(string aggregate, string entityId, IEnumerable<Edit> edits)
    {
        var o = new MergeOutcome();
        foreach (var e in edits)
        {
            if (Equals(e.Cloud, e.Incoming)) continue;
            if (!e.Allowed) { o.NotAllowed.Add(e.Field); continue; }
            if (e.NodeAt < e.CloudAt) { o.CloudNewer.Add(e.Field); continue; }
            e.Apply();
            db.SyncFieldTimes[(aggregate, entityId, e.Field)] = e.NodeAt;
            o.Applied.Add((e.Field, e.Cloud, e.Incoming));
        }
        return o;
    }

    private void Audit(Guid doctorId, Guid? actor, string action, object details) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), EntityType = AuditEntityType.Doctor, EntityId = doctorId, Action = action, PerformedByUserId = actor,
            PerformedAt = DateTimeOffset.UtcNow, Details = JsonSerializer.Serialize(details, SyncJson.Options)
        });

    private async Task<SyncOperationResult> FinishSelfEditAsync(Guid nodeId, SyncPushOperation op, Guid entityId, Guid doctorId, SelfActor actor, MergeOutcome m,
        bool remapped, string what, CancellationToken ct)
    {
        if (m.Applied.Count > 0 || m.Kept.Count > 0 || remapped)
            Audit(doctorId, actor.UserId, $"Sync: {what}", new
            {
                source = "sync", node_id = nodeId, op.OperationId, op.Aggregate, entity_id = op.EntityId, op.OperationType, actor_role = actor.Role.ToString(),
                applied = m.Applied.ToDictionary(a => a.Field, a => new { old = a.Old, @new = a.New }),
                kept_cloud_newer = m.CloudNewer, kept_not_allowed = m.NotAllowed, natural_key_remapped = remapped
            });
        string status; string? reason = null; string? message = null;
        if (m.Kept.Count == 0 && !remapped) status = m.Applied.Count > 0 ? SyncStatuses.Applied : SyncStatuses.Duplicate;
        else
        {
            status = m.Applied.Count > 0 || remapped && m.Kept.Count == 0 ? SyncStatuses.Merged : SyncStatuses.Conflict;
            reason = m.NotAllowed.Count > 0 ? SyncReasons.FieldNotAllowed : m.CloudNewer.Count > 0 ? SyncReasons.CloudFieldNewer : SyncReasons.NaturalKeyRemapped;
            message = m.NotAllowed.Count > 0 ? "Fields reserved for the admin keep the cloud value: " + string.Join(", ", m.NotAllowed)
                : m.CloudNewer.Count > 0 ? "The cloud copy of these fields is newer and was kept: " + string.Join(", ", m.CloudNewer)
                : "The cloud already had this row under another id; the cloud row was updated and its id is in 'current'.";
        }
        var r = await DoneFor(op, op.Aggregate, entityId, status, reason, message, ct, m.Kept.Count > 0 ? m.Kept : remapped ? ["id"] : null);
        return r;
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ── doctor ───────────────────────────────────────────────────────────
    private async Task<SyncOperationResult> DoctorSelfAsync(Guid nodeId, SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "Doctors cannot be deleted through sync.");
        var doctor = await db.Doctors.Include(d => d.StaffAccount).FirstOrDefaultAsync(d => d.DoctorId == id, ct);
        if (doctor is null) return Fail(op, SyncStatuses.Rejected, SyncReasons.CloudOwned, "Doctor accounts are created in the cloud; only edits to an existing doctor are accepted.");
        var (actor, fail) = await ResolveSelfActorAsync(op, id, ct);
        if (fail is not null) return fail;
        var values = SyncRows.Read(db, typeof(Doctor), op.Data);
        if (IdMismatch(values, "doctor_id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var allowed = actor!.IsAdmin ? DoctorAdminFields : DoctorSelfFields;
        var byName = values.ToDictionary(kv => SyncRows.Name(kv.Key), kv => kv.Value);

        JsonElement staffEl = default;
        var hasStaff = op.Data.TryGetProperty("staff_account", out staffEl) || op.Data.TryGetProperty("staff_accounts", out staffEl);
        hasStaff &= staffEl.ValueKind == JsonValueKind.Object;

        // validation of the fields this actor may actually change
        string? Bad(string field, string message) => allowed.Contains(field) ? message : null;
        string? bad = null;
        if (byName.TryGetValue("specialization", out var sp) && (Blank(sp as string) is not { Length: > 0 and <= 100 })) bad ??= Bad("specialization", "specialization is required (max 100 characters).");
        if (byName.TryGetValue("bio", out var bio) && (bio as string)?.Length > 2000) bad ??= Bad("bio", "bio is too long (max 2000).");
        foreach (var f in new[] { "license_number", "ptr_number", "s2_number" })
            if (byName.TryGetValue(f, out var ln) && (ln as string)?.Length > 50) bad ??= Bad(f, $"{f} is too long (max 50).");
        if (byName.TryGetValue("consultation_fee", out var fee) && fee is decimal d0 && (d0 < 0 || d0 > 1_000_000m)) bad ??= Bad("consultation_fee", "consultation_fee is out of range.");
        if (byName.TryGetValue("slot_duration_minutes", out var sd) && sd is int s1 && (s1 < 5 || s1 > 240)) bad ??= Bad("slot_duration_minutes", "slot_duration_minutes must be 5-240.");
        if (byName.TryGetValue("slot_capacity", out var sc) && sc is int s2 && (s2 < 1 || s2 > 50)) bad ??= Bad("slot_capacity", "slot_capacity must be 1-50.");
        if (byName.TryGetValue("daily_patient_limit", out var dl) && dl is int s3 && (s3 < 1 || s3 > 500)) bad ??= Bad("daily_patient_limit", "daily_patient_limit must be 1-500 or null.");
        string? newName = null, newContact = null; bool hasName = false, hasContact = false;
        if (hasStaff)
        {
            if (staffEl.TryGetProperty("full_name", out var fn) && fn.ValueKind == JsonValueKind.String) { hasName = true; newName = fn.GetString()?.Trim(); if (string.IsNullOrEmpty(newName) || newName.Length > 200) bad ??= Bad("staff.full_name", "full_name is required (max 200)."); }
            if (staffEl.TryGetProperty("contact_number", out var cn) && cn.ValueKind is JsonValueKind.String or JsonValueKind.Null) { hasContact = true; newContact = Blank(cn.ValueKind == JsonValueKind.String ? cn.GetString() : null); if (newContact?.Length > 50) bad ??= Bad("staff.contact_number", "contact_number is too long (max 50)."); }
        }
        if (bad is not null) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, bad);

        var stamps = await StampsAsync(SyncAggregates.Doctor, SyncAggregates.Id(id), ct);
        var staff = doctor.StaffAccount;
        DateTimeOffset CloudAt(string field)
        {
            if (stamps.TryGetValue(field, out var at)) return at;
            var staffField = field.StartsWith("staff.");
            var created = staffField ? staff?.CreatedAt ?? doctor.CreatedAt : doctor.CreatedAt;
            var updated = staffField ? staff?.UpdatedAt ?? doctor.UpdatedAt : doctor.UpdatedAt;
            return stamps.Keys.Any(k => k.StartsWith("staff.") == staffField) ? created : updated; // tracked row: never-edited field = created; legacy row: row update time
        }
        var edits = new List<Edit>();
        void Add(string field, object? cloud, object? incoming, Action apply) => edits.Add(new Edit(field, cloud, incoming, apply, NodeTime(op, field), CloudAt(field), allowed.Contains(field)));
        if (byName.ContainsKey("specialization")) Add("specialization", doctor.Specialization, Blank(byName["specialization"] as string), () => doctor.Specialization = Blank(byName["specialization"] as string)!);
        if (byName.ContainsKey("bio")) Add("bio", doctor.Bio, Blank(byName["bio"] as string), () => doctor.Bio = Blank(byName["bio"] as string));
        if (byName.ContainsKey("consultation_fee")) Add("consultation_fee", doctor.ConsultationFee, byName["consultation_fee"], () => doctor.ConsultationFee = (decimal)byName["consultation_fee"]!);
        if (byName.ContainsKey("license_number")) Add("license_number", doctor.LicenseNumber, Blank(byName["license_number"] as string), () => doctor.LicenseNumber = Blank(byName["license_number"] as string));
        if (byName.ContainsKey("ptr_number")) Add("ptr_number", doctor.PtrNumber, Blank(byName["ptr_number"] as string), () => doctor.PtrNumber = Blank(byName["ptr_number"] as string));
        if (byName.ContainsKey("s2_number")) Add("s2_number", doctor.S2Number, Blank(byName["s2_number"] as string), () => doctor.S2Number = Blank(byName["s2_number"] as string));
        if (byName.ContainsKey("slot_duration_minutes")) Add("slot_duration_minutes", doctor.SlotDurationMinutes, byName["slot_duration_minutes"], () => doctor.SlotDurationMinutes = (int)byName["slot_duration_minutes"]!);
        if (byName.ContainsKey("slot_capacity")) Add("slot_capacity", doctor.SlotCapacity, byName["slot_capacity"], () => doctor.SlotCapacity = (int)byName["slot_capacity"]!);
        if (byName.ContainsKey("daily_patient_limit")) Add("daily_patient_limit", doctor.DailyPatientLimit, byName["daily_patient_limit"], () => doctor.DailyPatientLimit = (int?)byName["daily_patient_limit"]);
        if (staff is not null && hasName) Add("staff.full_name", staff.FullName, newName, () => staff.FullName = newName!);
        if (staff is not null && hasContact) Add("staff.contact_number", staff.ContactNumber, newContact, () => staff.ContactNumber = newContact);

        var m = Merge(SyncAggregates.Doctor, SyncAggregates.Id(id), edits);
        var result = await FinishSelfEditAsync(nodeId, op, id, id, actor, m, false, "doctor profile edited", ct);
        if (result.Current is Dictionary<string, object?> cur && staff is not null)
            cur["staff_account"] = new Dictionary<string, object?> { ["staff_id"] = staff.StaffId, ["full_name"] = staff.FullName, ["contact_number"] = staff.ContactNumber };
        return result;
    }

    // ── doctor_schedule ──────────────────────────────────────────────────
    private async Task<SyncOperationResult> DoctorScheduleSelfAsync(Guid nodeId, SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "A weekday is switched off with is_active=false; schedule rows are never deleted.");
        var values = SyncRows.Read(db, typeof(DoctorSchedule), op.Data);
        if (IdMismatch(values, "id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var byName = values.ToDictionary(kv => SyncRows.Name(kv.Key), kv => kv.Value);
        var byId = await db.DoctorSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        Guid? doctorId = byId?.DoctorId ?? byName.GetValueOrDefault("doctor_id") as Guid?;
        if (doctorId is null || doctorId == Guid.Empty) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "doctor_id is required.");
        if (byId is not null && byName.TryGetValue("doctor_id", out var dv) && dv is Guid dg && dg != byId.DoctorId)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "doctor_id of an existing schedule row cannot change.");
        if (!await db.Doctors.AnyAsync(d => d.DoctorId == doctorId, ct)) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "Unknown doctor.");
        var (actor, fail) = await ResolveSelfActorAsync(op, doctorId.Value, ct);
        if (fail is not null) return fail;

        short? dow = byName.TryGetValue("day_of_week", out var dwv) && dwv is short sdw ? sdw : byId?.DayOfWeek;
        if (dow is null || dow < 0 || dow > 6) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "day_of_week must be 0 (Sunday) to 6 (Saturday).");
        if (byId is not null && byId.DayOfWeek != dow) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "day_of_week of an existing schedule row cannot change.");

        var row = byId ?? await db.DoctorSchedules.FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.DayOfWeek == dow, ct);
        var remapped = byId is null && row is not null;
        bool? inActive = byName.TryGetValue("is_active", out var ia) ? ia as bool? : null;
        TimeOnly? inStart = byName.TryGetValue("start_time", out var st) ? st as TimeOnly? : null;
        TimeOnly? inEnd = byName.TryGetValue("end_time", out var en) ? en as TimeOnly? : null;
        var active = inActive ?? row?.IsActive ?? true;
        var start = inStart ?? row?.StartTime; var end = inEnd ?? row?.EndTime;
        if (start is null || end is null) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "start_time and end_time are required.");
        if (active && start >= end) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "start_time must be before end_time on an active day.");

        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new DoctorSchedule { Id = id, DoctorId = doctorId.Value, DayOfWeek = dow.Value, IsActive = active, StartTime = start.Value, EndTime = end.Value, CreatedAt = now, UpdatedAt = now };
            db.DoctorSchedules.Add(row);
            var sid = SyncAggregates.Id(id);
            foreach (var f in new[] { "is_active", "start_time", "end_time" }) db.SyncFieldTimes[(SyncAggregates.DoctorSchedule, sid, f)] = NodeTime(op, f);
            var created = new MergeOutcome();
            created.Applied.Add(("created", null, $"day {dow} {start:HH:mm}-{end:HH:mm} active={active}"));
            return await FinishSelfEditAsync(nodeId, op, id, doctorId.Value, actor!, created, false, "doctor schedule changed", ct);
        }

        var rowId = SyncAggregates.Id(row.Id);
        var stamps = await StampsAsync(SyncAggregates.DoctorSchedule, rowId, ct);
        DateTimeOffset CloudAt(string f) => stamps.TryGetValue(f, out var at) ? at : stamps.Count > 0 ? row.CreatedAt : row.UpdatedAt;
        var edits = new List<Edit>();
        void Add(string f, object? cloud, object? incoming, Action apply) => edits.Add(new Edit(f, cloud, incoming, apply, NodeTime(op, f), CloudAt(f), true));
        if (inActive is not null) Add("is_active", row.IsActive, inActive, () => row.IsActive = inActive.Value);
        if (inStart is not null) Add("start_time", row.StartTime, inStart, () => row.StartTime = inStart.Value);
        if (inEnd is not null) Add("end_time", row.EndTime, inEnd, () => row.EndTime = inEnd.Value);
        var m = Merge(SyncAggregates.DoctorSchedule, rowId, edits);
        return await FinishSelfEditAsync(nodeId, op, row.Id, doctorId.Value, actor!, m, remapped, "doctor schedule changed", ct);
    }

    // ── doctor_blocked_date ──────────────────────────────────────────────
    private async Task<SyncOperationResult> DoctorBlockedDateSelfAsync(Guid nodeId, SyncPushOperation op, Guid id, CancellationToken ct)
    {
        var byId = await db.DoctorBlockedDates.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (op.IsDelete)
        {
            // The node's delete payload may be thin: the cloud row decides which doctor this is.
            if (byId is null) return Done0(op, SyncStatuses.Duplicate, null);
            var (da, df) = await ResolveSelfActorAsync(op, byId.DoctorId, ct);
            if (df is not null) return df;
            db.DoctorBlockedDates.Remove(byId);
            Audit(byId.DoctorId, da!.UserId, "Sync: blocked date removed", new { source = "sync", node_id = nodeId, op.OperationId, entity_id = op.EntityId, blocked_date = byId.BlockedDate, reason = byId.Reason, actor_role = da.Role.ToString() });
            await db.SaveChangesAsync(ct);
            return Done0(op, SyncStatuses.Applied, null);
        }
        var values = SyncRows.Read(db, typeof(DoctorBlockedDate), op.Data);
        if (IdMismatch(values, "id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var byName = values.ToDictionary(kv => SyncRows.Name(kv.Key), kv => kv.Value);
        Guid? doctorId = byId?.DoctorId ?? byName.GetValueOrDefault("doctor_id") as Guid?;
        if (doctorId is null || doctorId == Guid.Empty) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "doctor_id is required.");
        if (byId is not null && byName.TryGetValue("doctor_id", out var dv) && dv is Guid dg && dg != byId.DoctorId)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "doctor_id of an existing blocked date cannot change.");
        if (!await db.Doctors.AnyAsync(d => d.DoctorId == doctorId, ct)) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "Unknown doctor.");
        var (actor, fail) = await ResolveSelfActorAsync(op, doctorId.Value, ct);
        if (fail is not null) return fail;
        var reason = byName.ContainsKey("reason") ? Blank(byName["reason"] as string) : null;
        if (reason?.Length > 500) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "reason is too long (max 500).");
        DateOnly? date = byName.TryGetValue("blocked_date", out var bd) && bd is DateOnly dd && dd != default ? dd : byId?.BlockedDate;
        if (date is null) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "blocked_date is required.");
        if (byId is not null && byId.BlockedDate != date) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "blocked_date of an existing row cannot change; delete it and add a new one.");

        if (byId is null)
        {
            var sameDay = await db.DoctorBlockedDates.FirstOrDefaultAsync(b => b.DoctorId == doctorId && b.BlockedDate == date, ct);
            if (sameDay is not null)
            {
                // The cloud already blocks that day (another id): keep one row. Reason follows the field merge.
                var stampsK = await StampsAsync(SyncAggregates.DoctorBlockedDate, SyncAggregates.Id(sameDay.Id), ct);
                DateTimeOffset CloudAtK() => stampsK.TryGetValue("reason", out var at) ? at : sameDay.CreatedAt;
                var mk = byName.ContainsKey("reason")
                    ? Merge(SyncAggregates.DoctorBlockedDate, SyncAggregates.Id(sameDay.Id), [new Edit("reason", sameDay.Reason, reason, () => sameDay.Reason = reason, NodeTime(op, "reason"), CloudAtK(), true)])
                    : new MergeOutcome();
                return await FinishSelfEditAsync(nodeId, op, sameDay.Id, doctorId.Value, actor!, mk, true, "blocked date changed", ct);
            }
            var row = new DoctorBlockedDate
            {
                Id = id, DoctorId = doctorId.Value, BlockedDate = date.Value, Reason = reason,
                CreatedAt = byName.TryGetValue("created_at", out var ca) && ca is DateTimeOffset cdt && cdt != default ? cdt : DateTimeOffset.UtcNow
            };
            db.DoctorBlockedDates.Add(row);
            db.SyncFieldTimes[(SyncAggregates.DoctorBlockedDate, SyncAggregates.Id(id), "reason")] = NodeTime(op, "reason");
            var co = new MergeOutcome();
            co.Applied.Add(("created", null, $"{date:yyyy-MM-dd} {reason}"));
            return await FinishSelfEditAsync(nodeId, op, id, doctorId.Value, actor!, co, false, "blocked date added", ct);
        }

        var stamps = await StampsAsync(SyncAggregates.DoctorBlockedDate, SyncAggregates.Id(id), ct);
        DateTimeOffset CloudAt() => stamps.TryGetValue("reason", out var at) ? at : byId.CreatedAt;
        var m = byName.ContainsKey("reason")
            ? Merge(SyncAggregates.DoctorBlockedDate, SyncAggregates.Id(id), [new Edit("reason", byId.Reason, reason, () => byId.Reason = reason, NodeTime(op, "reason"), CloudAt(), true)])
            : new MergeOutcome();
        return await FinishSelfEditAsync(nodeId, op, id, doctorId.Value, actor!, m, false, "blocked date changed", ct);
    }
}
//JJQR - END
