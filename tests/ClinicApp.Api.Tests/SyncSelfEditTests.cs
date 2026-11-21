//JJQR - AI (Nov 21, 2026) offline self-edits: doctor / doctor_schedule / doctor_blocked_date pushed by the doctor himself (or an admin) — actor validation, field merge, audit
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class SyncSelfEditTests(ApiFixture api) : ApiTestBase(api)
{
    private sealed record Node(Guid Id, HttpClient Client) : IDisposable { public void Dispose() => Client.Dispose(); }
    private sealed record Doc(Guid UserId, Guid DoctorId);

    private async Task<Node> RegisterNodeAsync()
    {
        using var admin = F.As(W.Admin, "Admin");
        var id = Guid.NewGuid();
        var res = await admin.PostAsync("/api/sync/nodes", ApiFactory.Body(new { node_id = id, name = "Self-edit node", platform = "test", app_version = "0.0.1" }));
        await ShouldBe(HttpStatusCode.Created, res);
        var secret = (await JsonOf(res)).GetProperty("node_secret").GetString()!;
        var client = F.Anonymous();
        var tok = await client.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = id, node_secret = secret }));
        await ShouldBe(HttpStatusCode.OK, tok);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonOf(tok)).GetProperty("access_token").GetString());
        client.DefaultRequestHeaders.Add("X-Sync-Protocol", "1");
        return new Node(id, client);
    }

    private async Task<Doc> NewDoctor(StaffStatus status = StaffStatus.Active) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var uid = Guid.NewGuid();
        db.Users.Add(new User { Id = uid, Email = $"selfedit-{uid:N}@test.local", PasswordHash = "x", EmailConfirmed = true, CreatedAt = now });
        db.Profiles.Add(new Profile { Id = uid, Role = UserRole.Doctor, CreatedAt = now });
        var sid = Guid.NewGuid();
        db.StaffAccounts.Add(new StaffAccount { StaffId = sid, UserId = uid, FullName = "Dr Self Edit", Email = $"se-{sid:N}@test.local", Role = StaffRole.Doctor, Status = status, InvitedAt = now, CreatedAt = now, UpdatedAt = now });
        db.Doctors.Add(new Doctor { DoctorId = sid, Specialization = "GP", ConsultationFee = 450, Bio = "old bio", LicenseNumber = "LIC-1", SlotDurationMinutes = 15, SlotCapacity = 1, DailyPatientLimit = 20, CreatedAt = now.AddDays(-30), UpdatedAt = now.AddDays(-30) });
        await db.SaveChangesAsync();
        return new Doc(uid, sid);
    });

    private static StringContent Raw(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static object Op(string aggregate, Guid entityId, object? data, Guid? actor, string type = "update", DateTimeOffset? at = null, object? fieldTimes = null, Guid? opId = null)
    {
        var payload = new Dictionary<string, object?> { ["data"] = data };
        if (fieldTimes is not null) payload["field_updated_at"] = fieldTimes;
        var op = new Dictionary<string, object?>
        {
            ["operation_id"] = opId ?? Guid.NewGuid(), ["aggregate"] = aggregate, ["entity_id"] = entityId, ["operation_type"] = type,
            ["base_row_version"] = null, ["created_at"] = at ?? DateTimeOffset.UtcNow, ["payload"] = payload,
        };
        if (actor is not null) op["actor_user_id"] = actor;
        return op;
    }

    private async Task<JsonElement> PushOne(Node node, object op)
    {
        var res = await node.Client.PostAsync("/api/sync/push", Raw(new { operations = new[] { op } }));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("results")[0];
    }

    private static string Status(JsonElement r) => r.GetProperty("status").GetString()!;
    private static string? Reason(JsonElement r) => r.TryGetProperty("reason_code", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static List<string> Fields(JsonElement r) => r.TryGetProperty("conflict_fields", out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString()!).ToList() : [];

    private static Dictionary<string, object?> DoctorRow(Guid id, string specialization, string? bio, DateTimeOffset? updated = null, decimal? fee = null, string? license = null) => new()
    {
        ["doctor_id"] = id, ["specialization"] = specialization, ["bio"] = bio, ["updated_at"] = updated ?? DateTimeOffset.UtcNow,
        ["consultation_fee"] = fee ?? 450, ["license_number"] = license ?? "LIC-1",
    };

    private Task<Doctor> LoadDoctor(Guid id) => WithDb(db => db.Doctors.AsNoTracking().SingleAsync(d => d.DoctorId == id));
    private Task<int> AuditCount(Guid doctorId, string actionLike) => WithDb(db => db.AuditLogs.CountAsync(a => a.EntityType == AuditEntityType.Doctor && a.EntityId == doctorId && a.Action.Contains(actionLike)));

    // ── doctor ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Doctor_edits_own_bio_and_specialization_and_name_offline_with_an_audit_entry()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var data = DoctorRow(d.DoctorId, "Pediatrics", "New bio");
        data["staff_account"] = new Dictionary<string, object?> { ["full_name"] = "Dr Renamed", ["contact_number"] = "09171234567" };
        var r = await PushOne(node, Op("doctor", d.DoctorId, data, d.UserId));
        Assert.Equal("applied", Status(r));
        var row = await LoadDoctor(d.DoctorId);
        Assert.Equal("Pediatrics", row.Specialization);
        Assert.Equal("New bio", row.Bio);
        var staff = await WithDb(db => db.StaffAccounts.AsNoTracking().SingleAsync(s => s.StaffId == d.DoctorId));
        Assert.Equal("Dr Renamed", staff.FullName);
        Assert.Equal("09171234567", staff.ContactNumber);
        Assert.Equal(1, await AuditCount(d.DoctorId, "doctor profile edited"));
        var audit = await WithDb(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == d.DoctorId && a.Action.Contains("doctor profile edited")));
        Assert.Equal(d.UserId, audit.PerformedByUserId);
        Assert.Contains("specialization", audit.Details);
    }

    [Fact]
    public async Task Doctor_cannot_change_fee_license_or_limits_those_stay_cloud_owned_but_the_allowed_fields_still_apply()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var data = DoctorRow(d.DoctorId, "GP", "Edited bio", fee: 9999, license: "FAKE-LIC");
        data["slot_capacity"] = 40; data["daily_patient_limit"] = 400; data["slot_duration_minutes"] = 60;
        var r = await PushOne(node, Op("doctor", d.DoctorId, data, d.UserId));
        Assert.Equal("merged", Status(r));
        Assert.Equal("field_not_allowed", Reason(r));
        var kept = Fields(r);
        Assert.Contains("consultation_fee", kept); Assert.Contains("license_number", kept); Assert.Contains("slot_capacity", kept); Assert.Contains("daily_patient_limit", kept); Assert.Contains("slot_duration_minutes", kept);
        var row = await LoadDoctor(d.DoctorId);
        Assert.Equal("Edited bio", row.Bio);
        Assert.Equal(450m, row.ConsultationFee); Assert.Equal("LIC-1", row.LicenseNumber);
        Assert.Equal(1, row.SlotCapacity); Assert.Equal(20, row.DailyPatientLimit); Assert.Equal(15, row.SlotDurationMinutes);
        Assert.Equal(450m, r.GetProperty("current").GetProperty("consultation_fee").GetDecimal()); // the node overwrites its local copy with this
    }

    [Fact]
    public async Task Only_restricted_fields_differing_is_a_conflict_and_nothing_changes()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var r = await PushOne(node, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "GP", "old bio", fee: 5), d.UserId));
        Assert.Equal("conflict", Status(r));
        Assert.Equal("field_not_allowed", Reason(r));
        Assert.Equal(450m, (await LoadDoctor(d.DoctorId)).ConsultationFee);
    }

    [Fact]
    public async Task Admin_actor_may_change_the_admin_fields()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var data = DoctorRow(d.DoctorId, "GP", "old bio", fee: 650, license: "NEW-LIC");
        data["daily_patient_limit"] = 30;
        var r = await PushOne(node, Op("doctor", d.DoctorId, data, W.Admin.UserId));
        Assert.Equal("applied", Status(r));
        var row = await LoadDoctor(d.DoctorId);
        Assert.Equal(650m, row.ConsultationFee); Assert.Equal("NEW-LIC", row.LicenseNumber); Assert.Equal(30, row.DailyPatientLimit);
    }

    [Fact]
    public async Task Another_doctor_staff_unknown_deactivated_or_missing_actor_is_rejected_and_audited()
    {
        using var node = await RegisterNodeAsync();
        var a = await NewDoctor(); var b = await NewDoctor(); var gone = await NewDoctor(StaffStatus.Inactive);
        var data = DoctorRow(a.DoctorId, "Hacked", "hacked");
        var other = await PushOne(node, Op("doctor", a.DoctorId, data, b.UserId));
        Assert.Equal("rejected", Status(other)); Assert.Equal("actor_not_allowed", Reason(other));
        var staff = await PushOne(node, Op("doctor", a.DoctorId, data, W.Staff.UserId));
        Assert.Equal("rejected", Status(staff)); Assert.Equal("actor_not_allowed", Reason(staff));
        var unknown = await PushOne(node, Op("doctor", a.DoctorId, data, Guid.NewGuid()));
        Assert.Equal("rejected", Status(unknown)); Assert.Equal("actor_unknown", Reason(unknown));
        var none = await PushOne(node, Op("doctor", a.DoctorId, data, null));
        Assert.Equal("rejected", Status(none)); Assert.Equal("actor_required", Reason(none));
        var deactivated = await PushOne(node, Op("doctor", gone.DoctorId, DoctorRow(gone.DoctorId, "x", "x"), gone.UserId));
        Assert.Equal("rejected", Status(deactivated)); Assert.Equal("actor_unknown", Reason(deactivated));
        var row = await LoadDoctor(a.DoctorId);
        Assert.Equal("GP", row.Specialization); Assert.Equal("old bio", row.Bio);
        Assert.True(await AuditCount(a.DoctorId, "self-edit rejected") >= 4);
    }

    [Fact]
    public async Task Doctor_create_delete_and_profile_pushes_stay_cloud_owned()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var del = await PushOne(node, Op("doctor", d.DoctorId, null, d.UserId, "delete"));
        Assert.Equal("rejected", Status(del)); Assert.Equal("delete_not_allowed", Reason(del));
        var ghost = Guid.NewGuid();
        var create = await PushOne(node, Op("doctor", ghost, DoctorRow(ghost, "GP", null), W.Admin.UserId, "create"));
        Assert.Equal("rejected", Status(create)); Assert.Equal("cloud_owned", Reason(create));
        var profile = await PushOne(node, Op("profile", d.UserId, new { id = d.UserId, role = "Admin" }, d.UserId));
        Assert.Equal("rejected", Status(profile)); Assert.Equal("cloud_owned", Reason(profile));
        var staffAcct = await PushOne(node, Op("staff_account", d.DoctorId, new { staff_id = d.DoctorId, status = "Inactive" }, d.UserId));
        Assert.Equal("rejected", Status(staffAcct)); Assert.Equal("cloud_owned", Reason(staffAcct));
    }

    [Fact]
    public async Task Field_merge_newest_edit_wins_per_field_not_per_row()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        // a web edit (admin) of the bio happens now
        using (var admin = F.As(W.Admin, "Admin"))
        {
            var cur = await LoadDoctor(d.DoctorId);
            var put = await admin.PutAsync($"/api/doctors/{d.DoctorId}", ApiFactory.Body(new { doctor_id = d.DoctorId, specialization = "GP", consultation_fee = 450, bio = "web bio", license_number = "LIC-1", slot_duration_minutes = 15, slot_capacity = 1, daily_patient_limit = 20 }));
            await ShouldBe(HttpStatusCode.OK, put);
        }
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1); var inAMinute = DateTimeOffset.UtcNow.AddSeconds(30);
        // node edited bio an hour ago (older than the web edit) and specialization just now (never edited on the cloud)
        var r = await PushOne(node, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "Cardiology", "node bio"), d.UserId, fieldTimes: new Dictionary<string, object?> { ["bio"] = hourAgo, ["specialization"] = inAMinute }));
        Assert.Equal("merged", Status(r));
        Assert.Equal("cloud_field_newer", Reason(r));
        Assert.Equal(["bio"], Fields(r));
        var row = await LoadDoctor(d.DoctorId);
        Assert.Equal("web bio", row.Bio);
        Assert.Equal("Cardiology", row.Specialization);
        // a node edit that is newer than the web edit wins
        var win = await PushOne(node, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "Cardiology", "node bio 2"), d.UserId, fieldTimes: new Dictionary<string, object?> { ["bio"] = inAMinute.AddSeconds(5) }));
        Assert.Equal("applied", Status(win));
        Assert.Equal("node bio 2", (await LoadDoctor(d.DoctorId)).Bio);
        // and an older edit from a second offline node now loses against it
        var late = await PushOne(node, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "Cardiology", "stale bio"), d.UserId, fieldTimes: new Dictionary<string, object?> { ["bio"] = inAMinute }));
        Assert.Equal("conflict", Status(late)); Assert.Equal("cloud_field_newer", Reason(late));
        Assert.Equal("node bio 2", (await LoadDoctor(d.DoctorId)).Bio);
    }

    [Fact]
    public async Task A_node_clock_in_the_future_cannot_win_forever()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var r = await PushOne(node, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "GP", "future bio"), d.UserId, fieldTimes: new Dictionary<string, object?> { ["bio"] = DateTimeOffset.UtcNow.AddYears(5) }));
        Assert.Equal("applied", Status(r));
        // a normal edit a moment later still wins over the clamped stamp
        await Task.Delay(30);
        var next = await PushOne(node, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "GP", "present bio"), d.UserId));
        Assert.Equal("applied", Status(next));
        Assert.Equal("present bio", (await LoadDoctor(d.DoctorId)).Bio);
    }

    [Fact]
    public async Task Doctor_push_is_idempotent_and_an_identical_snapshot_is_a_duplicate()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var opId = Guid.NewGuid();
        var data = DoctorRow(d.DoctorId, "GP", "idem bio");
        var first = await PushOne(node, Op("doctor", d.DoctorId, data, d.UserId, opId: opId));
        var replay = await PushOne(node, Op("doctor", d.DoctorId, data, d.UserId, opId: opId));
        Assert.Equal("applied", Status(first)); Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(1, await AuditCount(d.DoctorId, "doctor profile edited"));
        var again = await PushOne(node, Op("doctor", d.DoctorId, data, d.UserId));
        Assert.Equal("duplicate", Status(again));
    }

    [Fact]
    public async Task Doctor_edits_are_visible_to_other_nodes_on_pull_but_not_echoed_to_the_writer()
    {
        using var writer = await RegisterNodeAsync(); using var other = await RegisterNodeAsync();
        var d = await NewDoctor();
        long since = await WithDb(db => Task.FromResult(db.SyncChanges.Any() ? db.SyncChanges.Max(c => c.Seq) : 0L));
        await PushOne(writer, Op("doctor", d.DoctorId, DoctorRow(d.DoctorId, "Pulled", "pulled bio"), d.UserId));
        async Task<bool> Sees(Node n)
        {
            var res = await n.Client.GetAsync($"/api/sync/pull?since={since}&limit=1000");
            await ShouldBe(HttpStatusCode.OK, res);
            return (await JsonOf(res)).GetProperty("changes").EnumerateArray().Any(c => c.GetProperty("aggregate").GetString() == "doctor" && c.GetProperty("entity_id").GetString() == d.DoctorId.ToString("D"));
        }
        Assert.True(await Sees(other));
        Assert.False(await Sees(writer));
    }

    [Fact]
    public async Task Node_outbox_shaped_partial_payload_with_extra_keys_is_accepted_and_only_the_sent_fields_change()
    {
        // ClinicNode's DoctorSelfEditService writes PayloadJson = changed fields + changed_fields/actor_user_id/field_updated_at inside data;
        // the sync client lifts actor_user_id and field_updated_at to the op / payload. The cloud must ignore the extra keys inside data.
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var stamp = DateTimeOffset.UtcNow.AddSeconds(2).ToString("O");
        var data = new Dictionary<string, object?>
        {
            ["doctor_id"] = d.DoctorId, ["bio"] = "partial bio", ["updated_at"] = DateTimeOffset.UtcNow,
            ["staff_account"] = new Dictionary<string, object?> { ["contact_number"] = "0917 555 0000" },
            ["changed_fields"] = new[] { "bio", "staff.contact_number" }, ["actor_user_id"] = d.UserId,
            ["field_updated_at"] = new Dictionary<string, object?> { ["bio"] = stamp, ["staff.contact_number"] = stamp },
        };
        var r = await PushOne(node, Op("doctor", d.DoctorId, data, d.UserId, fieldTimes: new Dictionary<string, object?> { ["bio"] = stamp, ["staff.contact_number"] = stamp }));
        Assert.Equal("applied", Status(r));
        var row = await LoadDoctor(d.DoctorId);
        Assert.Equal("partial bio", row.Bio);
        Assert.Equal("GP", row.Specialization);          // not sent → untouched
        Assert.Equal(450m, row.ConsultationFee);
        Assert.Equal("0917 555 0000", (await WithDb(db => db.StaffAccounts.AsNoTracking().SingleAsync(x => x.StaffId == d.DoctorId))).ContactNumber);

        // a schedule update that only carries one changed field, plus the node's extras
        var sid = Guid.NewGuid();
        await PushOne(node, Op("doctor_schedule", sid, SchedRow(sid, d.DoctorId, 5, true), d.UserId, "create"));
        var part = new Dictionary<string, object?> { ["id"] = sid, ["doctor_id"] = d.DoctorId, ["day_of_week"] = 5, ["end_time"] = "13:00:00", ["updated_at"] = DateTimeOffset.UtcNow, ["actor_user_id"] = d.UserId, ["changed_fields"] = new[] { "end_time" } };
        var u = await PushOne(node, Op("doctor_schedule", sid, part, d.UserId, fieldTimes: new Dictionary<string, object?> { ["end_time"] = DateTimeOffset.UtcNow.AddSeconds(2) }));
        Assert.Equal("applied", Status(u));
        var sched = await WithDb(db => db.DoctorSchedules.AsNoTracking().SingleAsync(x => x.Id == sid));
        Assert.Equal(new TimeOnly(13, 0), sched.EndTime); Assert.Equal(new TimeOnly(8, 0), sched.StartTime); Assert.True(sched.IsActive);
    }

    // ── doctor_schedule ──────────────────────────────────────────────────
    private static Dictionary<string, object?> SchedRow(Guid id, Guid doctor, int dow, bool active, string start = "08:00:00", string end = "17:00:00") => new()
    {
        ["id"] = id, ["doctor_id"] = doctor, ["day_of_week"] = dow, ["is_active"] = active, ["start_time"] = start, ["end_time"] = end, ["updated_at"] = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Schedule_create_update_and_key_remap()
    {
        using var node = await RegisterNodeAsync();
        var d = await NewDoctor();
        var rowId = Guid.NewGuid();
        var create = await PushOne(node, Op("doctor_schedule", rowId, SchedRow(rowId, d.DoctorId, 1, true), d.UserId, "create"));
        Assert.Equal("applied", Status(create));
        var saved = await WithDb(db => db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == rowId));
        Assert.Equal(1, saved.DayOfWeek); Assert.True(saved.IsActive);

        var upd = await PushOne(node, Op("doctor_schedule", rowId, SchedRow(rowId, d.DoctorId, 1, true, "09:00:00", "12:00:00"), d.UserId, fieldTimes: new Dictionary<string, object?> { ["start_time"] = DateTimeOffset.UtcNow.AddSeconds(5), ["end_time"] = DateTimeOffset.UtcNow.AddSeconds(5) }));
        Assert.Equal("applied", Status(upd));
        saved = await WithDb(db => db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == rowId));
        Assert.Equal(new TimeOnly(9, 0), saved.StartTime); Assert.Equal(new TimeOnly(12, 0), saved.EndTime);

        // a second row id for the same weekday (the node created its own before its first pull): one row stays, the cloud id wins
        var other = Guid.NewGuid();
        var remap = await PushOne(node, Op("doctor_schedule", other, SchedRow(other, d.DoctorId, 1, false, "09:00:00", "12:00:00"), d.UserId, "create", fieldTimes: new Dictionary<string, object?> { ["is_active"] = DateTimeOffset.UtcNow.AddSeconds(10) }));
        Assert.Equal("merged", Status(remap)); Assert.Equal("natural_key_remapped", Reason(remap));
        Assert.Equal(rowId.ToString(), remap.GetProperty("current").GetProperty("id").GetString());
        Assert.Equal(1, await WithDb(db => db.DoctorSchedules.CountAsync(s => s.DoctorId == d.DoctorId && s.DayOfWeek == 1)));
        Assert.False((await WithDb(db => db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == rowId))).IsActive);
    }

    [Fact]
    public async Task Schedule_rules_actor_delete_and_validation()
    {
        using var node = await RegisterNodeAsync();
        var a = await NewDoctor(); var b = await NewDoctor();
        var id = Guid.NewGuid();
        await PushOne(node, Op("doctor_schedule", id, SchedRow(id, a.DoctorId, 2, true), a.UserId, "create"));
        var foreign = Guid.NewGuid();
        var other = await PushOne(node, Op("doctor_schedule", foreign, SchedRow(foreign, a.DoctorId, 3, true), b.UserId, "create"));
        Assert.Equal("rejected", Status(other)); Assert.Equal("actor_not_allowed", Reason(other));
        Assert.False(await WithDb(db => db.DoctorSchedules.AnyAsync(s => s.Id == foreign)));
        var del = await PushOne(node, Op("doctor_schedule", id, null, a.UserId, "delete"));
        Assert.Equal("rejected", Status(del)); Assert.Equal("delete_not_allowed", Reason(del));
        var bad = Guid.NewGuid();
        var badTimes = await PushOne(node, Op("doctor_schedule", bad, SchedRow(bad, a.DoctorId, 4, true, "17:00:00", "08:00:00"), a.UserId, "create"));
        Assert.Equal("rejected", Status(badTimes)); Assert.Equal("invalid_payload", Reason(badTimes));
        var badDay = Guid.NewGuid();
        var badDow = await PushOne(node, Op("doctor_schedule", badDay, SchedRow(badDay, a.DoctorId, 9, true), a.UserId, "create"));
        Assert.Equal("rejected", Status(badDow)); Assert.Equal("invalid_payload", Reason(badDow));
        var noActor = await PushOne(node, Op("doctor_schedule", id, SchedRow(id, a.DoctorId, 2, false), null));
        Assert.Equal("rejected", Status(noActor)); Assert.Equal("actor_required", Reason(noActor));
        Assert.True((await WithDb(db => db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == id))).IsActive);
        var admin = await PushOne(node, Op("doctor_schedule", id, SchedRow(id, a.DoctorId, 2, false), W.Admin.UserId, fieldTimes: new Dictionary<string, object?> { ["is_active"] = DateTimeOffset.UtcNow.AddSeconds(5) }));
        Assert.Equal("applied", Status(admin));
    }

    // ── doctor_blocked_date ──────────────────────────────────────────────
    [Fact]
    public async Task Blocked_date_create_remap_reason_merge_and_delete()
    {
        using var node = await RegisterNodeAsync();
        var a = await NewDoctor(); var b = await NewDoctor();
        var id = Guid.NewGuid(); var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);
        object Row(Guid i, string? reason) => new Dictionary<string, object?> { ["id"] = i, ["doctor_id"] = a.DoctorId, ["blocked_date"] = day.ToString("yyyy-MM-dd"), ["reason"] = reason, ["created_at"] = DateTimeOffset.UtcNow };
        var create = await PushOne(node, Op("doctor_blocked_date", id, Row(id, "Conference"), a.UserId, "create"));
        Assert.Equal("applied", Status(create));
        Assert.Equal("Conference", (await WithDb(db => db.DoctorBlockedDates.AsNoTracking().SingleAsync(x => x.Id == id))).Reason);

        var dupId = Guid.NewGuid();
        var dup = await PushOne(node, Op("doctor_blocked_date", dupId, Row(dupId, "Conference"), a.UserId, "create"));
        Assert.Equal("merged", Status(dup)); Assert.Equal("natural_key_remapped", Reason(dup));
        Assert.Equal(id.ToString(), dup.GetProperty("current").GetProperty("id").GetString());
        Assert.Equal(1, await WithDb(db => db.DoctorBlockedDates.CountAsync(x => x.DoctorId == a.DoctorId && x.BlockedDate == day)));

        var newer = await PushOne(node, Op("doctor_blocked_date", id, Row(id, "Conference (moved)"), a.UserId, fieldTimes: new Dictionary<string, object?> { ["reason"] = DateTimeOffset.UtcNow.AddSeconds(5) }));
        Assert.Equal("applied", Status(newer));
        var older = await PushOne(node, Op("doctor_blocked_date", id, Row(id, "Old reason"), a.UserId, fieldTimes: new Dictionary<string, object?> { ["reason"] = DateTimeOffset.UtcNow.AddMinutes(-30) }));
        Assert.Equal("conflict", Status(older)); Assert.Equal("cloud_field_newer", Reason(older));
        Assert.Equal("Conference (moved)", (await WithDb(db => db.DoctorBlockedDates.AsNoTracking().SingleAsync(x => x.Id == id))).Reason);

        var wrong = await PushOne(node, Op("doctor_blocked_date", id, null, b.UserId, "delete"));
        Assert.Equal("rejected", Status(wrong)); Assert.Equal("actor_not_allowed", Reason(wrong));
        Assert.True(await WithDb(db => db.DoctorBlockedDates.AnyAsync(x => x.Id == id)));
        var del = await PushOne(node, Op("doctor_blocked_date", id, null, a.UserId, "delete"));
        Assert.Equal("applied", Status(del));
        Assert.False(await WithDb(db => db.DoctorBlockedDates.AnyAsync(x => x.Id == id)));
        var again = await PushOne(node, Op("doctor_blocked_date", id, null, a.UserId, "delete"));
        Assert.Equal("duplicate", Status(again));
        Assert.True(await AuditCount(a.DoctorId, "blocked date removed") == 1);
    }

    [Fact]
    public async Task Web_edits_are_stamped_per_field_so_they_take_part_in_the_merge()
    {
        var d = await NewDoctor();
        using var admin = F.As(W.Admin, "Admin");
        var put = await admin.PutAsync($"/api/doctors/{d.DoctorId}", ApiFactory.Body(new { doctor_id = d.DoctorId, specialization = "GP", consultation_fee = 450, bio = "web changed", license_number = "LIC-1", slot_duration_minutes = 15, slot_capacity = 1, daily_patient_limit = 20 }));
        await ShouldBe(HttpStatusCode.OK, put);
        var stamps = await WithDb(db => db.SyncFieldStamps.AsNoTracking().Where(s => s.Aggregate == "doctor" && s.EntityId == d.DoctorId.ToString()).Select(s => s.Field).ToListAsync());
        Assert.Contains("bio", stamps);
        Assert.DoesNotContain("consultation_fee", stamps); // unchanged fields are not stamped
    }
}
//JJQR - END
