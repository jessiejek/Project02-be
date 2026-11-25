//JJQR - AI (Nov 21, 2026) cloud sync: node auth, push (per-aggregate rules), pull (paged change log)
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class SyncTests(ApiFixture api) : ApiTestBase(api)
{
    // ── helpers ─────────────────────────────────────────────────────────
    //JJQR - AI (Nov 21, 2026) one stable, unique queue number per booking id (the cloud now reconciles duplicates)
    private static int _queueSeq;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> QueueNumbers = new();
    private static string QueueNoFor(Guid id) => QueueNumbers.GetOrAdd(id, _ => "R-" + (5000 + Interlocked.Increment(ref _queueSeq)));
    //JJQR - END
    private sealed record Node(Guid Id, string Secret, HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private async Task<Node> RegisterNodeAsync()
    {
        using var admin = F.As(W.Admin, "Admin");
        var id = Guid.NewGuid();
        var res = await admin.PostAsync("/api/sync/nodes", ApiFactory.Body(new { node_id = id, name = "Test node", platform = "test", app_version = "0.0.1" }));
        await ShouldBe(HttpStatusCode.Created, res);
        var secret = (await JsonOf(res)).GetProperty("node_secret").GetString()!;
        var client = F.Anonymous();
        var tok = await client.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = id, node_secret = secret }));
        await ShouldBe(HttpStatusCode.OK, tok);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonOf(tok)).GetProperty("access_token").GetString());
        client.DefaultRequestHeaders.Add("X-Sync-Protocol", "1");
        return new Node(id, secret, client);
    }

    private static StringContent Raw(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static object Op(string aggregate, Guid entityId, object? data, string type = "upsert", long? baseRv = null, Guid? opId = null, object? changedFields = null) => new Dictionary<string, object?>
    {
        ["operation_id"] = opId ?? Guid.NewGuid(),
        ["aggregate"] = aggregate,
        ["entity_id"] = entityId,
        ["operation_type"] = type,
        ["base_row_version"] = baseRv,
        ["created_at"] = DateTimeOffset.UtcNow,
        ["payload"] = new Dictionary<string, object?> { ["data"] = data, ["changed_fields"] = changedFields },
    };

    private async Task<List<JsonElement>> PushAsync(Node node, params object[] ops)
    {
        var res = await node.Client.PostAsync("/api/sync/push", Raw(new { operations = ops }));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("results").EnumerateArray().ToList();
    }

    private static string StatusOf(JsonElement r) => r.GetProperty("status").GetString()!;

    private static Dictionary<string, object?> PatientRow(Guid id, string code, string first = "Ana", DateTimeOffset? updatedAt = null) => new()
    {
        ["patient_id"] = id, ["patient_code"] = code, ["first_name"] = first, ["last_name"] = "Node",
        ["date_of_birth"] = "1950-04-02", ["sex"] = "Female", ["email"] = "", ["address"] = "Old address",
        ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = updatedAt ?? DateTimeOffset.UtcNow,
    };

    private Dictionary<string, object?> BookingDoc(Guid id, Guid patientId, string status, DateTimeOffset? updatedAt = null) => new()
    {
        ["booking"] = new Dictionary<string, object?>
        {
            ["booking_id"] = id, ["patient_id"] = patientId, ["doctor_id"] = W.DoctorA.RowId,
            ["appointment_date"] = ClinicApp.Domain.ClinicClock.Today.ToString("yyyy-MM-dd"), ["slot_start_time"] = "00:00:00", ["slot_end_time"] = "00:00:00",
            ["status"] = status, ["payment_mode"] = "PayAtClinic", ["queue_number"] = QueueNoFor(id), //JJQR - AI (Nov 21, 2026) unique numbers now that the cloud reconciles duplicates // JJQR - END
            ["consultation_fee_snapshot"] = 450, ["total_fee"] = 450, ["amount_due"] = 450, ["is_walk_in"] = true, ["visit_type"] = "New",
            ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = updatedAt ?? DateTimeOffset.UtcNow,
        },
        ["booking_services"] = Array.Empty<object>(),
    };

    private static Dictionary<string, object?> PaymentRow(Guid id, Guid bookingId, string status, decimal? received = null) => new()
    {
        ["payment_id"] = id, ["booking_id"] = bookingId, ["amount"] = 450, ["status"] = status,
        ["payment_method"] = status == "Paid" ? "Cash" : null, ["amount_received"] = received,
        ["confirmed_at"] = status == "Paid" ? DateTimeOffset.UtcNow : null,
        ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
    };

    private Dictionary<string, object?> ConsultDoc(Guid id, Guid bookingId, Guid patientId, string status, params string[] icd) => new()
    {
        ["consultation"] = new Dictionary<string, object?>
        {
            ["consultation_id"] = id, ["booking_id"] = bookingId, ["patient_id"] = patientId, ["doctor_id"] = W.DoctorA.RowId,
            ["status"] = status, ["chief_complaint"] = "Cough", ["assessment"] = "URTI",
            ["completed_at"] = status == "Completed" ? DateTimeOffset.UtcNow : null,
            ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
        },
        ["diagnoses"] = icd.Select(c => new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(), ["consultation_id"] = id, ["icd10_code"] = c, ["type"] = "Primary", ["created_at"] = DateTimeOffset.UtcNow
        }).ToList(),
        ["follow_up"] = new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(), ["consultation_id"] = id, ["patient_id"] = patientId, ["doctor_id"] = W.DoctorA.RowId,
            ["follow_up_date"] = ClinicApp.Domain.ClinicClock.Today.AddDays(7).ToString("yyyy-MM-dd"), ["status"] = "Pending",
            ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
        },
    };

    private Task<long> MaxSeqAsync() => WithDb(db => db.SyncChanges.AnyAsync().ContinueWith(_ => db.SyncChanges.Max(c => c.Seq)));

    private async Task<JsonElement> PullAsync(Node node, string query)
    {
        var res = await node.Client.GetAsync("/api/sync/pull?" + query);
        await ShouldBe(HttpStatusCode.OK, res);
        return await JsonOf(res);
    }

    private async Task<List<JsonElement>> PullAllSinceAsync(Node node, long since, bool includeOwn = false)
    {
        var all = new List<JsonElement>();
        var cursor = since.ToString();
        for (var i = 0; i < 50; i++)
        {
            var page = await PullAsync(node, $"since={cursor}&limit=1000&include_own={includeOwn.ToString().ToLowerInvariant()}");
            all.AddRange(page.GetProperty("changes").EnumerateArray());
            cursor = page.GetProperty("next_cursor").GetString()!;
            if (!page.GetProperty("has_more").GetBoolean()) break;
        }
        return all;
    }

    private async Task<Guid> PushNewPatientAsync(Node node)
    {
        var pid = Guid.NewGuid();
        var r = await PushAsync(node, Op("patient", pid, PatientRow(pid, "MF-T-0001")));
        Assert.Contains(StatusOf(r[0]), new[] { "applied", "merged" });
        return pid;
    }

    // ── node registration + auth ────────────────────────────────────────
    [Fact]
    public async Task Only_an_admin_can_register_a_node()
    {
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBeDenied(await staff.PostAsync("/api/sync/nodes", ApiFactory.Body(new { name = "x" })));
        using var anon = F.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/sync/nodes", ApiFactory.Body(new { name = "x" }))).StatusCode);
    }

    [Fact]
    public async Task Registered_node_gets_a_token_and_handshake_works()
    {
        using var node = await RegisterNodeAsync();
        var res = await node.Client.GetAsync("/api/sync/handshake");
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await JsonOf(res);
        Assert.Equal(1, body.GetProperty("protocol_version").GetInt32());
        Assert.Equal(node.Id, body.GetProperty("node_id").GetGuid());

        using var admin = F.As(W.Admin, "Admin");
        var dup = await admin.PostAsync("/api/sync/nodes", ApiFactory.Body(new { node_id = node.Id, name = "again" }));
        await ShouldBe(HttpStatusCode.Conflict, dup);
        Assert.Contains("node_exists", await dup.Content.ReadAsStringAsync());
        var list = await ListOf(await admin.GetAsync("/api/sync/nodes"));
        Assert.Contains(list, n => n.GetProperty("node_id").GetGuid() == node.Id);
        Assert.DoesNotContain("secret", (await admin.GetAsync("/api/sync/nodes")).Content.ReadAsStringAsync().Result);
    }

    [Fact]
    public async Task Wrong_secret_is_401_and_revoked_node_is_403()
    {
        using var node = await RegisterNodeAsync();
        using var anon = F.Anonymous();
        var bad = await anon.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = node.Id, node_secret = "nope" }));
        await ShouldBe(HttpStatusCode.Unauthorized, bad);
        Assert.Contains("invalid_node_credentials", await bad.Content.ReadAsStringAsync());

        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/sync/nodes/{node.Id}/revoke", null));

        var tok = await anon.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = node.Id, node_secret = node.Secret }));
        await ShouldBe(HttpStatusCode.Forbidden, tok);
        Assert.Contains("node_revoked", await tok.Content.ReadAsStringAsync());

        var pull = await node.Client.GetAsync("/api/sync/pull");
        await ShouldBe(HttpStatusCode.Forbidden, pull);
        Assert.Contains("node_revoked", await pull.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rotated_secret_replaces_the_old_one()
    {
        using var node = await RegisterNodeAsync();
        using var admin = F.As(W.Admin, "Admin");
        var rot = await admin.PostAsync($"/api/sync/nodes/{node.Id}/rotate-secret", null);
        await ShouldBe(HttpStatusCode.OK, rot);
        var fresh = (await JsonOf(rot)).GetProperty("node_secret").GetString();
        using var anon = F.Anonymous();
        await ShouldBe(HttpStatusCode.Unauthorized, await anon.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = node.Id, node_secret = node.Secret })));
        await ShouldBe(HttpStatusCode.OK, await anon.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = node.Id, node_secret = fresh })));
    }

    [Fact]
    public async Task Node_token_is_refused_outside_sync_and_user_token_is_refused_on_sync()
    {
        using var node = await RegisterNodeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await node.Client.GetAsync("/api/bookings")).StatusCode);
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBeDenied(await admin.GetAsync("/api/sync/pull"));
    }

    [Fact]
    public async Task Unsupported_protocol_is_426()
    {
        using var node = await RegisterNodeAsync();
        node.Client.DefaultRequestHeaders.Remove("X-Sync-Protocol");
        node.Client.DefaultRequestHeaders.Add("X-Sync-Protocol", "99");
        var res = await node.Client.GetAsync("/api/sync/handshake");
        Assert.Equal((HttpStatusCode)426, res.StatusCode);
    }

    [Fact]
    public async Task Batch_validation()
    {
        using var node = await RegisterNodeAsync();
        await ShouldBe(HttpStatusCode.BadRequest, await node.Client.PostAsync("/api/sync/push", Raw(new { operations = Array.Empty<object>() })));
        var tooMany = Enumerable.Range(0, 201).Select(_ => Op("audit_log", Guid.NewGuid(), new { })).ToArray();
        await ShouldBe(HttpStatusCode.BadRequest, await node.Client.PostAsync("/api/sync/push", Raw(new { operations = tooMany })));
    }

    // ── push: idempotency + generic rejections ──────────────────────────
    [Fact]
    public async Task Same_operation_id_is_applied_once_and_replayed()
    {
        using var node = await RegisterNodeAsync();
        var pid = Guid.NewGuid();
        var opId = Guid.NewGuid();
        var op = Op("patient", pid, PatientRow(pid, "MF-T-0042"), "create", null, opId);
        var first = (await PushAsync(node, op))[0];
        var second = (await PushAsync(node, op))[0];
        Assert.Equal(StatusOf(first), StatusOf(second));
        Assert.False(first.GetProperty("replayed").GetBoolean());
        Assert.True(second.GetProperty("replayed").GetBoolean());
        Assert.Equal(1, await WithDb(db => db.Patients.CountAsync(p => p.PatientId == pid)));
        Assert.Equal(1, await WithDb(db => db.SyncOperations.CountAsync(o => o.OperationId == opId)));
    }

    [Fact]
    public async Task Cloud_owned_and_unknown_aggregates_are_rejected()
    {
        using var node = await RegisterNodeAsync();
        var r = await PushAsync(node,
            Op("clinic_setting", Guid.NewGuid(), new { clinic_name = "Hacked" }),
            Op("soap_template", Guid.NewGuid(), new { title = "x" }),
            Op("spaceship", Guid.NewGuid(), new { }));
        Assert.Equal("cloud_owned", r[0].GetProperty("reason_code").GetString());
        Assert.Equal("cloud_owned", r[1].GetProperty("reason_code").GetString());
        Assert.Equal("unknown_aggregate", r[2].GetProperty("reason_code").GetString());
        Assert.All(r, x => Assert.Equal("rejected", StatusOf(x)));
    }

    // ── patient ─────────────────────────────────────────────────────────
    [Fact]
    public async Task Patient_with_temporary_code_gets_a_cloud_code()
    {
        using var node = await RegisterNodeAsync();
        var pid = Guid.NewGuid();
        var r = (await PushAsync(node, Op("patient", pid, PatientRow(pid, "MF-T-0007"), "create")))[0];
        Assert.Equal("merged", StatusOf(r));
        Assert.Contains("patient_code", r.GetProperty("conflict_fields").EnumerateArray().Select(x => x.GetString()));
        var code = r.GetProperty("current").GetProperty("patient_code").GetString()!;
        Assert.StartsWith("MF-", code);
        Assert.DoesNotContain("MF-T-", code);
        var saved = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == pid));
        Assert.Equal(node.Id, saved.OriginNodeId);
        Assert.True(saved.RowVersion >= 1);
    }

    [Fact]
    public async Task Patient_update_with_current_base_applies_and_stale_older_edit_keeps_cloud()
    {
        using var node = await RegisterNodeAsync();
        var pid = Guid.NewGuid();
        var code = "MF-N" + pid.ToString("N")[..6];
        var created = (await PushAsync(node, Op("patient", pid, PatientRow(pid, code), "create")))[0];
        Assert.Equal("applied", StatusOf(created));
        var rv = created.GetProperty("row_version").GetInt64();

        var upd = PatientRow(pid, code, "Anabel");
        var r1 = (await PushAsync(node, Op("patient", pid, upd, "update", rv)))[0];
        Assert.Equal("applied", StatusOf(r1));
        Assert.Equal("Anabel", await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.FirstName).SingleAsync()));

        // Cloud edits the address afterwards (web) — the node then pushes an older edit from a stale base.
        await WithDb(async db => { var p = await db.Patients.SingleAsync(x => x.PatientId == pid); p.Address = "Cloud address"; await db.SaveChangesAsync(); return 0; });
        var stale = PatientRow(pid, code, "Anabel", DateTimeOffset.UtcNow.AddHours(-1));
        stale["address"] = "Node address";
        var r2 = (await PushAsync(node, Op("patient", pid, stale, "update", rv, changedFields: new[] { "address" })))[0];
        Assert.Equal("merged", StatusOf(r2));
        Assert.Contains("address", r2.GetProperty("conflict_fields").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Cloud address", await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.Address).SingleAsync()));

        // A newer node edit wins field-by-field.
        var newer = PatientRow(pid, code, "Anabel", DateTimeOffset.UtcNow.AddMinutes(5));
        newer["address"] = "Node address";
        var r3 = (await PushAsync(node, Op("patient", pid, newer, "update", rv, changedFields: new[] { "address" })))[0];
        Assert.Equal("applied", StatusOf(r3));
        Assert.Equal("Node address", await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.Address).SingleAsync()));
    }

    [Fact]
    public async Task Patient_delete_is_rejected()
    {
        using var node = await RegisterNodeAsync();
        var pid = await PushNewPatientAsync(node);
        var r = (await PushAsync(node, Op("patient", pid, null, "delete")))[0];
        Assert.Equal("rejected", StatusOf(r));
        Assert.Equal("delete_not_allowed", r.GetProperty("reason_code").GetString());
    }

    // ── booking / queue ─────────────────────────────────────────────────
    [Fact]
    public async Task Booking_before_its_patient_is_retry_and_not_consumed()
    {
        using var node = await RegisterNodeAsync();
        var pid = Guid.NewGuid();
        var bid = Guid.NewGuid();
        var bookingOp = Op("booking", bid, BookingDoc(bid, pid, "CheckedIn"), "create");
        var r1 = (await PushAsync(node, bookingOp))[0];
        Assert.Equal("retry", StatusOf(r1));
        Assert.Equal("missing_dependency", r1.GetProperty("reason_code").GetString());

        var r2 = await PushAsync(node, Op("patient", pid, PatientRow(pid, "MF-T-1")), bookingOp);
        Assert.Equal("applied", StatusOf(r2[1]));
        var b = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == bid));
        Assert.Equal(BookingStatus.CheckedIn, b.Status);
        Assert.StartsWith("R-", b.QueueNumber);
        Assert.Equal(node.Id, b.OriginNodeId);
    }

    [Fact]
    public async Task Booking_status_follows_the_state_machine()
    {
        using var node = await RegisterNodeAsync();
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "CheckedIn"), "create")))[0]));

        // legal forward move: applied
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "InProgress"), "status")))[0]));

        // cloud moves further (Completed); node then pushes an older state → cloud kept
        await WithDb(async db => { var x = await db.Bookings.SingleAsync(b => b.BookingId == bid); x.Status = BookingStatus.Completed; await db.SaveChangesAsync(); return 0; });
        var r = (await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "InProgress"), "status")))[0];
        Assert.Equal("merged", StatusOf(r)); // InProgress → Completed is legal, so the cloud is further along
        Assert.Equal("cloud_further", r.GetProperty("reason_code").GetString());
        Assert.Equal(BookingStatus.Completed, await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.Status).SingleAsync()));

        // illegal both ways (Completed vs Cancelled) → conflict, cloud kept
        var r2 = (await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "Cancelled"), "cancel")))[0];
        Assert.Equal("conflict", StatusOf(r2));
        Assert.Equal("illegal_transition", r2.GetProperty("reason_code").GetString());
        Assert.Equal("Completed", r2.GetProperty("current").GetProperty("booking").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Booking_cloud_further_keeps_cloud_status_but_applies_other_fields()
    {
        using var node = await RegisterNodeAsync();
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var created = (await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "CheckedIn"), "create")))[0];
        // web: front desk calls the patient (CheckedIn → InProgress)
        await WithDb(async db => { var x = await db.Bookings.SingleAsync(b => b.BookingId == bid); x.Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        var doc = BookingDoc(bid, pid, "CheckedIn", DateTimeOffset.UtcNow.AddMinutes(1));
        ((Dictionary<string, object?>)doc["booking"]!)["notes"] = "node note";
        var r = (await PushAsync(node, Op("booking", bid, doc, "update", created.GetProperty("row_version").GetInt64())))[0];
        Assert.Equal("merged", StatusOf(r));
        Assert.Equal("cloud_further", r.GetProperty("reason_code").GetString());
        var b = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == bid));
        Assert.Equal(BookingStatus.InProgress, b.Status);
        Assert.Equal("node note", b.Notes);
    }

    //JJQR - AI (2026-11-25) - why: GAV-4 "Move to today" changes the date on the cloud; a node that never saw it (old base) keeps its other edits but
    // cannot put the old date back
    [Fact]
    public async Task Booking_moved_to_today_on_the_cloud_keeps_its_date_against_an_old_node_copy()
    {
        using var node = await RegisterNodeAsync();
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var yesterday = ClinicApp.Domain.ClinicClock.Today.AddDays(-1).ToString("yyyy-MM-dd");
        var doc0 = BookingDoc(bid, pid, "CheckedIn");
        ((Dictionary<string, object?>)doc0["booking"]!)["appointment_date"] = yesterday;
        var created = (await PushAsync(node, Op("booking", bid, doc0, "create")))[0];
        using (var doc = F.As(W.DoctorA, "Doctor"))
            await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{bid}/move-to-today", null));

        var stale = BookingDoc(bid, pid, "CheckedIn", DateTimeOffset.UtcNow.AddMinutes(1));
        ((Dictionary<string, object?>)stale["booking"]!)["appointment_date"] = yesterday;
        ((Dictionary<string, object?>)stale["booking"]!)["notes"] = "node note";
        var r = (await PushAsync(node, Op("booking", bid, stale, "update", created.GetProperty("row_version").GetInt64())))[0];
        Assert.Equal("merged", StatusOf(r));
        var b = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == bid));
        Assert.Equal(ClinicApp.Domain.ClinicClock.Today, b.AppointmentDate);
        Assert.Equal("node note", b.Notes);
    }
    //JJQR - END

    // ── payment ─────────────────────────────────────────────────────────
    private async Task<(Guid Booking, Guid Payment)> NodeVisitAsync(Node node)
    {
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var payId = Guid.NewGuid();
        var r = await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "CheckedIn"), "create"), Op("payment", payId, PaymentRow(payId, bid, "Unpaid"), "create"));
        Assert.All(r, x => Assert.Equal("applied", StatusOf(x)));
        return (bid, payId);
    }

    [Fact]
    public async Task Payment_node_confirm_applies_and_exact_amount_and_no_refund_rules_hold()
    {
        using var node = await RegisterNodeAsync();
        var (bid, pay) = await NodeVisitAsync(node);

        var mismatch = (await PushAsync(node, Op("payment", pay, PaymentRow(pay, bid, "Paid", 500), "confirm")))[0];
        Assert.Equal("rejected", StatusOf(mismatch));
        Assert.Equal("amount_mismatch", mismatch.GetProperty("reason_code").GetString());

        var ok = (await PushAsync(node, Op("payment", pay, PaymentRow(pay, bid, "Paid", 450), "confirm")))[0];
        Assert.Equal("applied", StatusOf(ok));
        var saved = await WithDb(db => db.Payments.AsNoTracking().SingleAsync(p => p.PaymentId == pay));
        Assert.Equal(PaymentStatus.Paid, saved.Status);
        Assert.Equal(node.Id, saved.OriginNodeId);

        var refund = (await PushAsync(node, Op("payment", pay, PaymentRow(pay, bid, "Refunded"), "update")))[0];
        Assert.Equal("refund_offline_forbidden", refund.GetProperty("reason_code").GetString());
    }

    //JJQR - AI (Nov 25, 2026) - owner: an offline desktop payment (incl. Mark received, which writes no local audit row) gets its audit row when it syncs; once
    [Fact]
    public async Task Payment_confirmed_offline_writes_one_audit_row_when_it_syncs()
    {
        using var node = await RegisterNodeAsync();
        var (bid, pay) = await NodeVisitAsync(node);
        var row = PaymentRow(pay, bid, "Paid", 450);
        row["or_number"] = "BK-000123";
        row["confirmed_by_user_id"] = W.Staff.UserId;
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("payment", pay, row, "confirm")))[0]));
        var audits = await WithDb(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == pay && a.Action.StartsWith("Paid (")).ToListAsync());
        var a = Assert.Single(audits);
        Assert.Equal("Paid (synced from desktop)", a.Action);
        Assert.Equal(W.Staff.UserId, a.PerformedByUserId);
        Assert.Contains("OR BK-000123", a.Details);
        // the same operation sent again (retry) adds nothing
        await PushAsync(node, Op("payment", pay, row, "confirm"));
        Assert.Single(await WithDb(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == pay && a.Action.StartsWith("Paid (")).ToListAsync()));
    }

    [Fact]
    public async Task Doctor_mark_received_done_offline_is_audited_as_the_doctors_cash_when_it_syncs()
    {
        using var node = await RegisterNodeAsync();
        var (bid, pay) = await NodeVisitAsync(node);
        var row = PaymentRow(pay, bid, "Paid", 450);
        row["confirm_notes"] = "Cash received by the doctor (marked on the doctor dashboard)."; // ClinicNode.Core DoctorDashboardService.MarkReceivedNote
        row["confirmed_by_user_id"] = W.DoctorA.UserId;
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("payment", pay, row, "confirm")))[0]));
        var a = Assert.Single(await WithDb(db => db.AuditLogs.AsNoTracking().Where(x => x.EntityId == pay && x.Action.StartsWith("Paid (")).ToListAsync()));
        Assert.Equal("Paid (marked by doctor, synced from desktop)", a.Action);
        Assert.Equal(W.DoctorA.UserId, a.PerformedByUserId);
        Assert.Contains("no OR number", a.Details);
        Assert.Equal(ClinicApp.Api.Controllers.PaymentsController.DoctorReceivedNote, ClinicApp.Infrastructure.Sync.SyncPushEngine.DoctorMarkedReceivedNote);
    }
    //JJQR - END

    [Fact]
    public async Task Payment_has_a_single_writer_node()
    {
        using var owner = await RegisterNodeAsync();
        using var other = await RegisterNodeAsync();
        var (bid, pay) = await NodeVisitAsync(owner);
        var r = (await PushAsync(other, Op("payment", pay, PaymentRow(pay, bid, "Paid", 450), "confirm")))[0];
        Assert.Equal("rejected", StatusOf(r));
        Assert.Equal("not_owner", r.GetProperty("reason_code").GetString());
        Assert.Equal(PaymentStatus.Unpaid, await WithDb(db => db.Payments.Where(p => p.PaymentId == pay).Select(p => p.Status).SingleAsync()));
    }

    [Fact]
    public async Task Payment_cloud_edit_is_overwritten_and_flagged_but_cloud_refund_wins()
    {
        using var node = await RegisterNodeAsync();
        var (bid, pay) = await NodeVisitAsync(node);
        var rv = await WithDb(db => db.Payments.Where(p => p.PaymentId == pay).Select(p => p.RowVersion).SingleAsync());
        await WithDb(async db => { var p = await db.Payments.SingleAsync(x => x.PaymentId == pay); p.ConfirmNotes = "cloud edit"; await db.SaveChangesAsync(); return 0; });

        var r = (await PushAsync(node, Op("payment", pay, PaymentRow(pay, bid, "Paid", 450), "confirm", rv)))[0];
        Assert.Equal("applied", StatusOf(r));
        Assert.Equal("cloud_edit_overwritten", r.GetProperty("reason_code").GetString());
        Assert.True(await WithDb(db => db.AuditLogs.AnyAsync(a => a.EntityId == pay && a.Action == "Sync conflict flagged")));

        await WithDb(async db => { var p = await db.Payments.SingleAsync(x => x.PaymentId == pay); p.Status = PaymentStatus.Refunded; await db.SaveChangesAsync(); return 0; });
        var r2 = (await PushAsync(node, Op("payment", pay, PaymentRow(pay, bid, "Paid", 450), "confirm")))[0];
        Assert.Equal("conflict", StatusOf(r2));
        Assert.Equal("cloud_further", r2.GetProperty("reason_code").GetString());
    }

    [Fact]
    public async Task Node_payment_replaces_an_untouched_cloud_payment_row_for_the_booking()
    {
        using var node = await RegisterNodeAsync();
        var (bid, cloudPay) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        var nodePay = Guid.NewGuid();
        var r = (await PushAsync(node, Op("payment", nodePay, PaymentRow(nodePay, bid, "Paid", 450), "create")))[0];
        Assert.Equal("applied", StatusOf(r));
        var rows = await WithDb(db => db.Payments.AsNoTracking().Where(p => p.BookingId == bid).ToListAsync());
        Assert.Equal(nodePay, Assert.Single(rows).PaymentId);
    }

    // ── consultation ────────────────────────────────────────────────────
    [Fact]
    public async Task Consultation_node_wins_while_draft_then_is_immutable_once_finished()
    {
        using var node = await RegisterNodeAsync();
        var (bid, _) = await NodeVisitAsync(node);
        var pid = await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.PatientId).SingleAsync());
        var cid = Guid.NewGuid();

        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("consultation", cid, ConsultDoc(cid, bid, pid, "Draft", "J06.9", "R05"), "create")))[0]));
        Assert.Equal(2, await WithDb(db => db.ConsultationDiagnoses.CountAsync(d => d.ConsultationId == cid)));
        Assert.Equal(1, await WithDb(db => db.FollowUps.CountAsync(d => d.ConsultationId == cid)));

        // Draft edit replaces the children as a set.
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("consultation", cid, ConsultDoc(cid, bid, pid, "Draft", "J20.9"), "update")))[0]));
        var codes = await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == cid).Select(d => d.Icd10Code).ToListAsync());
        Assert.Equal(new[] { "J20.9" }, codes);
        Assert.Equal(1, await WithDb(db => db.FollowUps.CountAsync(d => d.ConsultationId == cid)));

        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("consultation", cid, ConsultDoc(cid, bid, pid, "Completed", "J20.9"), "complete")))[0]));
        Assert.Equal(ConsultationStatus.Completed, await WithDb(db => db.Consultations.Where(c => c.ConsultationId == cid).Select(c => c.Status).SingleAsync()));

        var late = (await PushAsync(node, Op("consultation", cid, ConsultDoc(cid, bid, pid, "Draft", "A00"), "update")))[0];
        Assert.Equal("conflict", StatusOf(late));
        Assert.Equal("finished_immutable", late.GetProperty("reason_code").GetString());
        Assert.Equal("Completed", late.GetProperty("current").GetProperty("consultation").GetProperty("status").GetString());
        Assert.Equal(new[] { "J20.9" }, await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == cid).Select(d => d.Icd10Code).ToListAsync()));
    }

    [Fact]
    public async Task Consultation_node_draft_replaces_a_cloud_draft_but_not_a_cloud_finished_one()
    {
        using var node = await RegisterNodeAsync();
        // Seeded ConsultationA belongs to BookingA; make a fresh booking + cloud draft instead.
        var (bid, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        var cloudId = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Consultations.Add(new Consultation { ConsultationId = cloudId, BookingId = bid, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId, Status = ConsultationStatus.Draft, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(); return 0;
        });
        var nodeId = Guid.NewGuid();
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("consultation", nodeId, ConsultDoc(nodeId, bid, W.PatientA.RowId, "Draft", "J06.9"), "create")))[0]));
        Assert.False(await WithDb(db => db.Consultations.AnyAsync(c => c.ConsultationId == cloudId)));

        await WithDb(async db => { var c = await db.Consultations.SingleAsync(x => x.ConsultationId == nodeId); c.Status = ConsultationStatus.Completed; await db.SaveChangesAsync(); return 0; });
        var other = Guid.NewGuid();
        var r = (await PushAsync(node, Op("consultation", other, ConsultDoc(other, bid, W.PatientA.RowId, "Draft"), "create")))[0];
        Assert.Equal("conflict", StatusOf(r));
        Assert.Equal("finished_immutable", r.GetProperty("reason_code").GetString());
    }

    // ── append-only ─────────────────────────────────────────────────────
    [Fact]
    public async Task Vitals_and_audit_logs_are_append_only()
    {
        using var node = await RegisterNodeAsync();
        var (bid, _) = await NodeVisitAsync(node);
        var pid = await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.PatientId).SingleAsync());
        var template = await WithDb(db => db.VitalFieldTemplates.Select(t => t.TemplateId).FirstAsync());
        var vid = Guid.NewGuid();
        object Vital(Guid id, string value) => new { id, booking_id = bid, patient_id = pid, template_id = template, value, recorded_at = ClinicApp.Domain.ClinicClock.Today.ToString("yyyy-MM-dd") };

        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("vital_reading", vid, Vital(vid, "120/80"), "create")))[0]));
        Assert.Equal("duplicate", StatusOf((await PushAsync(node, Op("vital_reading", vid, Vital(vid, "120/80"), "create")))[0]));
        Assert.Equal("append_only", (await PushAsync(node, Op("vital_reading", vid, Vital(vid, "140/90"), "update")))[0].GetProperty("reason_code").GetString());
        var again = Guid.NewGuid();
        Assert.Equal("already_recorded", (await PushAsync(node, Op("vital_reading", again, Vital(again, "130/85"), "create")))[0].GetProperty("reason_code").GetString());
        Assert.Equal("120/80", await WithDb(db => db.PatientVitalReadings.Where(v => v.Id == vid).Select(v => v.Value).SingleAsync()));

        var aid = Guid.NewGuid();
        object Audit() => new { id = aid, entity_type = "Booking", entity_id = bid, action = "Checked in (offline)", performed_at = DateTimeOffset.UtcNow };
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("audit_log", aid, Audit(), "create")))[0]));
        Assert.Equal("duplicate", StatusOf((await PushAsync(node, Op("audit_log", aid, Audit(), "create")))[0]));
        Assert.Equal("append_only", (await PushAsync(node, Op("audit_log", aid, null, "delete")))[0].GetProperty("reason_code").GetString());
    }

    // ── pull ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Pull_returns_web_changes_and_skips_the_nodes_own_unless_asked()
    {
        using var node = await RegisterNodeAsync();
        var since = await MaxSeqAsync();
        var own = await PushNewPatientAsync(node);
        var (webBooking, webPay) = await WithDb(db => World.NewBookingAsync(db, W.PatientB.RowId, W.DoctorA.RowId));

        var changes = await PullAllSinceAsync(node, since);
        Assert.DoesNotContain(changes, c => c.GetProperty("entity_id").GetString() == own.ToString());
        var b = Assert.Single(changes, c => c.GetProperty("aggregate").GetString() == "booking" && c.GetProperty("entity_id").GetString() == webBooking.ToString());
        Assert.Equal("upsert", b.GetProperty("operation").GetString());
        Assert.Equal(webBooking, b.GetProperty("data").GetProperty("booking").GetProperty("booking_id").GetGuid());
        Assert.True(b.GetProperty("row_version").GetInt64() >= 1);
        Assert.Contains(changes, c => c.GetProperty("aggregate").GetString() == "payment" && c.GetProperty("entity_id").GetString() == webPay.ToString());

        var withOwn = await PullAllSinceAsync(node, since, includeOwn: true);
        var mine = Assert.Single(withOwn, c => c.GetProperty("entity_id").GetString() == own.ToString());
        Assert.Equal(node.Id, mine.GetProperty("origin_node_id").GetGuid());
    }

    [Fact]
    public async Task Pull_pages_with_a_cursor_and_rejects_a_bad_one()
    {
        using var node = await RegisterNodeAsync();
        var since = await MaxSeqAsync();
        for (var i = 0; i < 3; i++) await WithDb(db => World.NewBookingAsync(db, W.PatientB.RowId, W.DoctorA.RowId));
        var page1 = await PullAsync(node, $"since={since}&limit=1");
        Assert.True(page1.GetProperty("has_more").GetBoolean());
        Assert.Single(page1.GetProperty("changes").EnumerateArray());
        var c1 = page1.GetProperty("next_cursor").GetString()!;
        var page2 = await PullAsync(node, $"since={c1}&limit=1");
        Assert.True(long.Parse(page2.GetProperty("next_cursor").GetString()!) > long.Parse(c1));

        await ShouldBe(HttpStatusCode.BadRequest, await node.Client.GetAsync("/api/sync/pull?since=abc"));
        await ShouldBe(HttpStatusCode.BadRequest, await node.Client.GetAsync("/api/sync/pull?limit=0"));
    }

    [Fact]
    public async Task Pull_from_zero_bootstraps_cloud_owned_data_without_patient_accounts()
    {
        using var node = await RegisterNodeAsync();
        var all = await PullAllSinceAsync(node, 0);
        Assert.Contains(all, c => c.GetProperty("aggregate").GetString() == "clinic_setting");
        Assert.Contains(all, c => c.GetProperty("aggregate").GetString() == "doctor");
        var users = all.Where(c => c.GetProperty("aggregate").GetString() == "user").Select(c => c.GetProperty("entity_id").GetString()).ToList();
        Assert.Contains(W.DoctorA.UserId.ToString(), users);
        Assert.DoesNotContain(W.PatientA.UserId.ToString(), users);
        var doctorUser = all.First(c => c.GetProperty("aggregate").GetString() == "user" && c.GetProperty("entity_id").GetString() == W.DoctorA.UserId.ToString());
        Assert.True(doctorUser.GetProperty("data").TryGetProperty("password_hash", out _));
    }

    [Fact]
    public async Task Pull_emits_a_tombstone_for_a_deleted_row()
    {
        using var node = await RegisterNodeAsync();
        var since = await MaxSeqAsync();
        var serviceId = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Services.Add(new Service { ServiceId = serviceId, Name = "Temp " + serviceId.ToString()[..4], Price = 1, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            db.Services.Remove(await db.Services.SingleAsync(s => s.ServiceId == serviceId));
            await db.SaveChangesAsync(); return 0;
        });
        var changes = await PullAllSinceAsync(node, since);
        var s = Assert.Single(changes, c => c.GetProperty("entity_id").GetString() == serviceId.ToString());
        Assert.Equal("delete", s.GetProperty("operation").GetString());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task Web_edit_bumps_row_version_and_child_change_shows_up_as_its_consultation()
    {
        using var node = await RegisterNodeAsync();
        var since = await MaxSeqAsync();
        var before = await WithDb(db => db.Bookings.Where(b => b.BookingId == W.BookingB).Select(b => b.RowVersion).SingleAsync());
        await WithDb(async db => { var b = await db.Bookings.SingleAsync(x => x.BookingId == W.BookingB); b.Notes = "bump " + Guid.NewGuid(); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(before + 1, await WithDb(db => db.Bookings.Where(b => b.BookingId == W.BookingB).Select(b => b.RowVersion).SingleAsync()));

        await WithDb(async db =>
        {
            db.ConsultationDiagnoses.Add(new ConsultationDiagnosis { Id = Guid.NewGuid(), ConsultationId = W.ConsultationB, Icd10Code = "R51", Type = DiagnosisType.Secondary, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(); return 0;
        });
        var changes = await PullAllSinceAsync(node, since);
        var c = Assert.Single(changes, x => x.GetProperty("aggregate").GetString() == "consultation" && x.GetProperty("entity_id").GetString() == W.ConsultationB.ToString());
        Assert.Contains(c.GetProperty("data").GetProperty("diagnoses").EnumerateArray(), d => d.GetProperty("icd10_code").GetString() == "R51");
    }

    //JJQR - AI (Nov 21, 2026) part B: amendments and allergies
    private static long RowVersionOf(JsonElement r) => r.GetProperty("row_version").GetInt64();

    private async Task<(Guid Cid, Guid Bid, Guid Pid, Guid Pay, long Rv)> CompletedVisitAsync(Node node)
    {
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var pay = Guid.NewGuid();
        await PushAsync(node, Op("booking", bid, BookingDoc(bid, pid, "CheckedIn"), "create"), Op("payment", pay, PaymentRow(pay, bid, "Unpaid"), "create"));
        var cid = Guid.NewGuid();
        var r = (await PushAsync(node, Op("consultation", cid, ConsultDoc(cid, bid, pid, "Completed", "J20.9"), "complete")))[0];
        Assert.Equal("applied", StatusOf(r));
        return (cid, bid, pid, pay, RowVersionOf(r));
    }

    [Fact]
    public async Task Amendment_applies_to_a_finished_consultation_and_is_audited()
    {
        using var node = await RegisterNodeAsync();
        var v = await CompletedVisitAsync(node);

        // an ordinary edit of a finished visit is still refused
        var plain = (await PushAsync(node, Op("consultation", v.Cid, ConsultDoc(v.Cid, v.Bid, v.Pid, "Completed", "A00"), "update", v.Rv)))[0];
        Assert.Equal("finished_immutable", plain.GetProperty("reason_code").GetString());

        var amended = ConsultDoc(v.Cid, v.Bid, v.Pid, "Amended", "J20.9", "R05");
        var r = (await PushAsync(node, Op("consultation", v.Cid, amended, "update", v.Rv)))[0];
        Assert.Equal("applied", StatusOf(r));
        Assert.True(RowVersionOf(r) > v.Rv);
        Assert.Equal(ConsultationStatus.Amended, await WithDb(db => db.Consultations.Where(c => c.ConsultationId == v.Cid).Select(c => c.Status).SingleAsync()));
        Assert.Equal(new[] { "J20.9", "R05" }, (await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == v.Cid).Select(d => d.Icd10Code!).ToListAsync())).Order().ToArray());
        var audit = await WithDb(db => db.AuditLogs.Where(a => a.EntityId == v.Cid && a.Action == "Amended").Select(a => a.Details).ToListAsync());
        var line = Assert.Single(audit);
        Assert.Contains("diagnoses", line);
        Assert.DoesNotContain("J20.9", line); // sections only, no clinical text

        // the node was the last writer, so its next amendment needs no fresh base; Amended → Completed is refused
        var back = (await PushAsync(node, Op("consultation", v.Cid, ConsultDoc(v.Cid, v.Bid, v.Pid, "Completed", "J20.9"), "update", RowVersionOf(r))))[0];
        Assert.Equal("finished_immutable", back.GetProperty("reason_code").GetString());
        var again = ConsultDoc(v.Cid, v.Bid, v.Pid, "Amended", "J20.9");
        var r2 = (await PushAsync(node, Op("consultation", v.Cid, again, "update", null)))[0];
        Assert.Equal("applied", StatusOf(r2));
        Assert.Equal(new[] { "J20.9" }, await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == v.Cid).Select(d => d.Icd10Code!).ToListAsync()));
    }

    [Fact]
    public async Task Amendment_on_a_stale_copy_conflicts_and_an_identical_resend_is_a_duplicate()
    {
        using var node = await RegisterNodeAsync();
        var v = await CompletedVisitAsync(node);

        // the cloud changes the visit after the node last saw it
        await WithDb(async db => { var c = await db.Consultations.SingleAsync(x => x.ConsultationId == v.Cid); c.DoctorNotes = "cloud note"; await db.SaveChangesAsync(); return 0; });
        var stale = (await PushAsync(node, Op("consultation", v.Cid, ConsultDoc(v.Cid, v.Bid, v.Pid, "Amended", "X99"), "update", v.Rv)))[0];
        Assert.Equal("conflict", StatusOf(stale));
        Assert.Equal("stale_amendment", stale.GetProperty("reason_code").GetString());
        Assert.Equal("Completed", stale.GetProperty("current").GetProperty("consultation").GetProperty("status").GetString());
        Assert.DoesNotContain("X99", await WithDb(db => db.ConsultationDiagnoses.Where(d => d.ConsultationId == v.Cid).Select(d => d.Icd10Code!).ToListAsync()));

        // with the current row_version it applies; the same amendment again changes nothing → duplicate
        var cur = await WithDb(db => db.Consultations.Where(c => c.ConsultationId == v.Cid).Select(c => c.RowVersion).SingleAsync());
        var amended = ConsultDoc(v.Cid, v.Bid, v.Pid, "Amended", "J20.9");
        var ok = (await PushAsync(node, Op("consultation", v.Cid, amended, "update", cur)))[0];
        Assert.Equal("applied", StatusOf(ok));
        // re-send exactly what the cloud now holds (what a node that pulled it would hold): nothing changes → duplicate
        var snapshot = await WithDb(async db => (await ClinicApp.Infrastructure.Sync.SyncDocuments.BuildAsync(db, "consultation", v.Cid.ToString()))!);
        var echoed = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(snapshot.Data, ClinicApp.Infrastructure.Sync.SyncJson.Options))!;
        var dup = (await PushAsync(node, Op("consultation", v.Cid, echoed, "update", snapshot.RowVersion)))[0];
        Assert.Equal("duplicate", StatusOf(dup));
        Assert.Equal(1, await WithDb(db => db.AuditLogs.CountAsync(a => a.EntityId == v.Cid && a.Action == "Amended")));
    }

    [Fact]
    public async Task Amendment_cannot_contradict_a_settled_payment_or_change_the_visit_identity()
    {
        using var node = await RegisterNodeAsync();
        var v = await CompletedVisitAsync(node);
        await WithDb(async db =>
        {
            (await db.Consultations.SingleAsync(c => c.ConsultationId == v.Cid)).PfDecision = "Charge";
            var pay = await db.Payments.SingleAsync(p => p.PaymentId == v.Pay);
            pay.Status = PaymentStatus.Paid; pay.AmountReceived = 450; pay.PaymentMethod = PaymentMethod.Cash; pay.ConfirmedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(); return 0;
        });
        var cur = await WithDb(db => db.Consultations.Where(c => c.ConsultationId == v.Cid).Select(c => c.RowVersion).SingleAsync());
        var doc = ConsultDoc(v.Cid, v.Bid, v.Pid, "Amended", "J20.9");
        ((Dictionary<string, object?>)doc["consultation"]!)["pf_decision"] = "Waive";
        ((Dictionary<string, object?>)doc["consultation"]!)["pf_waive_reason"] = "charity";
        var r = (await PushAsync(node, Op("consultation", v.Cid, doc, "update", cur)))[0];
        Assert.Equal("conflict", StatusOf(r));
        Assert.Equal("payment_settled", r.GetProperty("reason_code").GetString());
        Assert.Equal(ConsultationStatus.Completed, await WithDb(db => db.Consultations.Where(c => c.ConsultationId == v.Cid).Select(c => c.Status).SingleAsync()));

        var other = ConsultDoc(v.Cid, v.Bid, W.PatientB.RowId, "Amended", "J20.9");
        var rej = (await PushAsync(node, Op("consultation", v.Cid, other, "update", cur)))[0];
        Assert.Equal("rejected", StatusOf(rej));
        Assert.Equal("invalid_payload", rej.GetProperty("reason_code").GetString());
    }

    private static Dictionary<string, object?> Allergy(string name, string? reaction = null, bool nkda = false, Guid? id = null) => new()
    {
        ["id"] = id ?? Guid.NewGuid(), ["allergen"] = name, ["reaction"] = reaction, ["severity"] = null, ["is_nkda"] = nkda,
    };

    private static Dictionary<string, object?> PatientWithAllergies(Guid pid, string code, params Dictionary<string, object?>[] allergies)
    {
        var row = PatientRow(pid, code);
        row["allergies"] = allergies.Select(a => { var c = new Dictionary<string, object?>(a) { ["patient_id"] = pid }; return c; }).ToList();
        return row;
    }

    private Task<List<string>> AllergyNamesAsync(Guid pid) =>
        WithDb(db => db.PatientAllergies.Where(a => a.PatientId == pid).Select(a => a.Allergen).ToListAsync()).ContinueWith(t => t.Result.Order().ToList());

    [Fact]
    public async Task Allergies_replace_when_the_node_saw_the_latest_copy_and_union_when_it_did_not()
    {
        using var node = await RegisterNodeAsync();
        var pid = Guid.NewGuid();
        // create: NKDA is dropped because a real allergen is present
        var created = (await PushAsync(node, Op("patient", pid, PatientWithAllergies(pid, "MF-T-" + pid.ToString()[..6], Allergy("Penicillin", "Rash"), Allergy("NKDA", nkda: true)), "create")))[0];
        Assert.Equal("merged", StatusOf(created)); // the temporary code was replaced
        Assert.Equal(new[] { "Penicillin" }, await AllergyNamesAsync(pid));
        var rv = await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.RowVersion).SingleAsync());
        var code = await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.PatientCode).SingleAsync());

        // the node saw the latest copy: its list replaces the cloud's (a removal is honoured)
        var replace = (await PushAsync(node, Op("patient", pid, PatientWithAllergies(pid, code, Allergy("Sulfa")), "update", rv)))[0];
        Assert.Equal("applied", StatusOf(replace));
        Assert.Equal(new[] { "Sulfa" }, await AllergyNamesAsync(pid));
        var rv2 = RowVersionOf(replace);
        Assert.True(rv2 > rv);

        // the cloud adds Latex through the web API; the node (still on the old copy) removes Sulfa and adds Aspirin
        using (var staff = F.As(W.Staff, "Staff"))
            await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/patients/{pid}/allergies",
                ApiFactory.Body(new[] { new { allergen = "Sulfa", is_nkda = false }, new { allergen = "Latex", is_nkda = false } })));
        Assert.True(await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.RowVersion).SingleAsync()) > rv2);
        var union = (await PushAsync(node, Op("patient", pid, PatientWithAllergies(pid, code, Allergy("Aspirin")), "update", rv2)))[0];
        Assert.Equal("merged", StatusOf(union));
        Assert.Equal("allergy_union", union.GetProperty("reason_code").GetString());
        Assert.Contains("allergies", union.GetProperty("conflict_fields").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "Aspirin", "Latex", "Sulfa" }, await AllergyNamesAsync(pid));
        Assert.Equal(3, union.GetProperty("current").GetProperty("allergies").GetArrayLength());

        // a stale "no known allergies" can never wipe real allergies
        var nkda = (await PushAsync(node, Op("patient", pid, PatientWithAllergies(pid, code, Allergy("NKDA", nkda: true)), "update", rv2)))[0];
        Assert.Equal("merged", StatusOf(nkda));
        Assert.Equal(new[] { "Aspirin", "Latex", "Sulfa" }, await AllergyNamesAsync(pid));

        // a patient push without the key leaves allergies alone; null/[] with the latest copy clears them
        var cur = await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.RowVersion).SingleAsync());
        await PushAsync(node, Op("patient", pid, PatientRow(pid, code, "Renamed"), "update", cur));
        Assert.Equal(3, (await AllergyNamesAsync(pid)).Count);
        var cur2 = await WithDb(db => db.Patients.Where(p => p.PatientId == pid).Select(p => p.RowVersion).SingleAsync());
        var cleared = (await PushAsync(node, Op("patient", pid, PatientWithAllergies(pid, code), "update", cur2)))[0];
        Assert.Equal("applied", StatusOf(cleared));
        Assert.Empty(await AllergyNamesAsync(pid));
    }

    [Fact]
    public async Task Allergy_changes_made_in_the_cloud_reach_a_node_as_a_patient_change()
    {
        using var node = await RegisterNodeAsync();
        var since = await MaxSeqAsync();
        using (var staff = F.As(W.Staff, "Staff"))
            await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/patients/{W.PatientB.RowId}/allergies",
                ApiFactory.Body(new[] { new { allergen = "Shellfish " + Guid.NewGuid().ToString()[..4], reaction = "Hives", is_nkda = false } })));
        var changes = await PullAllSinceAsync(node, since);
        var c = Assert.Single(changes, x => x.GetProperty("aggregate").GetString() == "patient" && x.GetProperty("entity_id").GetString() == W.PatientB.RowId.ToString());
        Assert.Contains(c.GetProperty("data").GetProperty("allergies").EnumerateArray(), a => a.GetProperty("allergen").GetString()!.StartsWith("Shellfish"));
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) queue-number reconciliation (contract §5.2)
    private Dictionary<string, object?> QueueBookingDoc(Guid id, Guid patientId, string date, string queueNumber, string status = "CheckedIn")
    {
        var doc = BookingDoc(id, patientId, status);
        var row = (Dictionary<string, object?>)doc["booking"]!;
        row["appointment_date"] = date;
        row["queue_number"] = queueNumber;
        return doc;
    }

    private Task<Guid> CloudBookingAsync(string date, string queueNumber, BookingStatus status) => WithDb(async db =>
    {
        var (id, _) = await World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId);
        var b = await db.Bookings.SingleAsync(x => x.BookingId == id);
        b.AppointmentDate = DateOnly.Parse(date); b.QueueNumber = queueNumber; b.Status = status;
        await db.SaveChangesAsync();
        return id;
    });

    private Task<List<string?>> NumbersOnAsync(string date) =>
        WithDb(db => db.Bookings.Where(b => b.AppointmentDate == DateOnly.Parse(date)).Select(b => b.QueueNumber).ToListAsync());

    [Fact]
    public async Task Queue_node_number_is_kept_and_the_cloud_counter_moves_past_it()
    {
        using var node = await RegisterNodeAsync();
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var r = (await PushAsync(node, Op("booking", bid, QueueBookingDoc(bid, pid, "2099-03-01", "R-005"), "create")))[0];
        Assert.Equal("applied", StatusOf(r));
        Assert.Equal("R-005", await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.QueueNumber).SingleAsync()));
        // the cloud never issues 1..5 again for that day and lane
        Assert.Equal("R-006", await WithDb(db => ClinicApp.Infrastructure.QueueSequencer.NextAsync(db, DateOnly.Parse("2099-03-01"), "R", default)));
        // the other lane is independent
        var sid = Guid.NewGuid();
        Assert.Equal("applied", StatusOf((await PushAsync(node, Op("booking", sid, QueueBookingDoc(sid, pid, "2099-03-01", "S-002"), "create")))[0]));
        Assert.Equal("S-003", await WithDb(db => ClinicApp.Infrastructure.QueueSequencer.NextAsync(db, DateOnly.Parse("2099-03-01"), "S", default)));
    }

    [Fact]
    public async Task Queue_clash_with_an_online_booking_nobody_was_served_under_renumbers_the_cloud_booking()
    {
        using var node = await RegisterNodeAsync();
        var online = await CloudBookingAsync("2099-03-02", "R-003", BookingStatus.Confirmed);
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var since = await MaxSeqAsync();

        var r = (await PushAsync(node, Op("booking", bid, QueueBookingDoc(bid, pid, "2099-03-02", "R-003"), "create")))[0];
        Assert.Equal("applied", StatusOf(r));
        Assert.Equal("R-003", await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.QueueNumber).SingleAsync()));
        var moved = await WithDb(db => db.Bookings.Where(b => b.BookingId == online).Select(b => b.QueueNumber).SingleAsync());
        Assert.NotEqual("R-003", moved);
        Assert.StartsWith("R-", moved);
        Assert.True(int.Parse(moved![2..]) > 3);
        var numbers = await NumbersOnAsync("2099-03-02");
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Equal(1, await WithDb(db => db.AuditLogs.CountAsync(a => a.EntityId == online && a.Action == "Queue number reassigned")));

        // the renumbered online booking reaches the node on pull
        var changes = await PullAllSinceAsync(node, since);
        var c = Assert.Single(changes, x => x.GetProperty("aggregate").GetString() == "booking" && x.GetProperty("entity_id").GetString() == online.ToString());
        Assert.Equal(moved, c.GetProperty("data").GetProperty("booking").GetProperty("queue_number").GetString());
    }

    [Fact]
    public async Task Queue_clash_with_a_booking_already_being_served_renumbers_the_incoming_one()
    {
        using var node = await RegisterNodeAsync();
        var live = await CloudBookingAsync("2099-03-03", "S-002", BookingStatus.CheckedIn);
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var r = (await PushAsync(node, Op("booking", bid, QueueBookingDoc(bid, pid, "2099-03-03", "S-002"), "create")))[0];
        Assert.Equal("merged", StatusOf(r));
        Assert.Equal("queue_number_reassigned", r.GetProperty("reason_code").GetString());
        Assert.Contains("queue_number", r.GetProperty("conflict_fields").EnumerateArray().Select(x => x.GetString()));
        var saved = await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.QueueNumber).SingleAsync());
        Assert.NotEqual("S-002", saved);
        Assert.StartsWith("S-", saved);
        Assert.Equal(saved, r.GetProperty("current").GetProperty("booking").GetProperty("queue_number").GetString());
        Assert.Equal("S-002", await WithDb(db => db.Bookings.Where(b => b.BookingId == live).Select(b => b.QueueNumber).SingleAsync()));
        var numbers = await NumbersOnAsync("2099-03-03");
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Queue_clash_between_two_nodes_first_push_wins()
    {
        using var first = await RegisterNodeAsync();
        using var second = await RegisterNodeAsync();
        var p1 = await PushNewPatientAsync(first);
        var p2 = await PushNewPatientAsync(second);
        var b1 = Guid.NewGuid(); var b2 = Guid.NewGuid();
        Assert.Equal("applied", StatusOf((await PushAsync(first, Op("booking", b1, QueueBookingDoc(b1, p1, "2099-03-04", "R-004"), "create")))[0]));
        var r = (await PushAsync(second, Op("booking", b2, QueueBookingDoc(b2, p2, "2099-03-04", "R-004"), "create")))[0];
        Assert.Equal("merged", StatusOf(r));
        Assert.Equal("queue_number_reassigned", r.GetProperty("reason_code").GetString());
        Assert.Equal("R-004", await WithDb(db => db.Bookings.Where(b => b.BookingId == b1).Select(b => b.QueueNumber).SingleAsync()));
        var numbers = await NumbersOnAsync("2099-03-04");
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Queue_number_change_on_update_needs_the_current_copy_and_is_reconciled()
    {
        using var node = await RegisterNodeAsync();
        var pid = await PushNewPatientAsync(node);
        var bid = Guid.NewGuid();
        var created = (await PushAsync(node, Op("booking", bid, QueueBookingDoc(bid, pid, "2099-03-05", "R-010"), "create")))[0];
        var rv = RowVersionOf(created);
        var other = await CloudBookingAsync("2099-03-05", "R-011", BookingStatus.Confirmed);

        // node re-numbers its booking to R-011 while holding the current copy: it keeps R-011, the unserved cloud booking moves
        var ok = (await PushAsync(node, Op("booking", bid, QueueBookingDoc(bid, pid, "2099-03-05", "R-011"), "update", rv)))[0];
        Assert.Equal("applied", StatusOf(ok));
        Assert.Equal("R-011", await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.QueueNumber).SingleAsync()));
        Assert.NotEqual("R-011", await WithDb(db => db.Bookings.Where(b => b.BookingId == other).Select(b => b.QueueNumber).SingleAsync()));

        // a stale copy cannot change the number
        var stale = (await PushAsync(node, Op("booking", bid, QueueBookingDoc(bid, pid, "2099-03-05", "R-020"), "update", rv)))[0];
        Assert.Equal("merged", StatusOf(stale));
        Assert.Contains("queue_number", stale.GetProperty("conflict_fields").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("R-011", await WithDb(db => db.Bookings.Where(b => b.BookingId == bid).Select(b => b.QueueNumber).SingleAsync()));
        var numbers = await NumbersOnAsync("2099-03-05");
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) lookup catalogs are pullable, including the bootstrap backfill
    private static readonly string[] CatalogAggregates =
    {
        "icd10_code", "medicine", "lab_test_catalog", "soap_phrase", "soap_template", "medical_certificate_template",
        "doctor_diagnosis_template", "prescription_template", "vital_field_template", "doctor_favorite_medicine",
        "doctor_builtin_template_copy", "service", "doctor", "doctor_service", "doctor_schedule", "clinic_setting",
        "clinic_operating_hour", "clinic_accepted_payment_method", "announcement", "doctor_day_status"
    };

    private static int CountRows(ClinicApp.Infrastructure.ClinicAppDbContext db, Type clr) =>
        ((System.Collections.IEnumerable)typeof(EntityFrameworkQueryableExtensions).GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!
            .MakeGenericMethod(clr).Invoke(null, new[] { typeof(DbContext).GetMethod("Set", Type.EmptyTypes)!.MakeGenericMethod(clr).Invoke(db, null) })!)
            .Cast<object>().Count();

    [Fact]
    public async Task Pull_from_the_start_emits_every_catalog_row_in_the_database()
    {
        using var node = await RegisterNodeAsync();
        var changes = await PullAllSinceAsync(node, 0);
        foreach (var name in CatalogAggregates)
        {
            var expected = await WithDb(db => Task.FromResult(CountRows(db, ClinicApp.Infrastructure.Sync.SyncAggregates.PullOnly[name])));
            var got = changes.Where(c => c.GetProperty("aggregate").GetString() == name && c.GetProperty("operation").GetString() == "upsert")
                .Select(c => c.GetProperty("entity_id").GetString()).Distinct().Count();
            Assert.True(expected > 0 || name is not ("icd10_code" or "medicine" or "lab_test_catalog" or "doctor" or "clinic_setting"),
                $"{name} has no rows in the test database");
            Assert.True(got == expected, $"{name}: pull emitted {got} but the database has {expected}");
        }
        var icd = changes.First(c => c.GetProperty("aggregate").GetString() == "icd10_code" && c.GetProperty("entity_id").GetString() == "J45.9");
        var data = icd.GetProperty("data");
        Assert.Equal("J45.9", data.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("description").GetString()));
        Assert.True(data.TryGetProperty("level", out _) && data.TryGetProperty("is_leaf", out _));
    }

    [Fact]
    public async Task Catalog_backfill_on_a_database_with_no_change_log_restores_the_full_catalog()
    {
        using var node = await RegisterNodeAsync();
        var before = (await PullAllSinceAsync(node, 0)).Count(c => c.GetProperty("operation").GetString() == "upsert" && c.GetProperty("aggregate").GetString() is "icd10_code" or "medicine" or "lab_test_catalog");
        Assert.True(before > 100);
        // as on a database that existed before sync: catalogs present, change log empty for them
        await WithDb(async db =>
        {
            await db.SyncChanges.Where(c => c.Aggregate == "icd10_code" || c.Aggregate == "medicine" || c.Aggregate == "lab_test_catalog").ExecuteDeleteAsync();
            return 0;
        });
        Assert.Equal(0, (await PullAllSinceAsync(node, 0)).Count(c => c.GetProperty("aggregate").GetString() is "icd10_code" or "medicine" or "lab_test_catalog"));
        await WithDb(async db => { await ClinicApp.Infrastructure.Sync.SyncBootstrap.BackfillAsync(db); return 0; });
        var after = (await PullAllSinceAsync(node, 0)).Count(c => c.GetProperty("operation").GetString() == "upsert" && c.GetProperty("aggregate").GetString() is "icd10_code" or "medicine" or "lab_test_catalog");
        Assert.True(before == after, $"before {before} after {after}");
    }
}
//JJQR - END
