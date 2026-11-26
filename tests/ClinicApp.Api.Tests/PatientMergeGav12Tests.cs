//JJQR - AI (2026-11-26) - GAV-12 why: "Merge to existing patient" — the doctor merges a duplicate (jese rubi) into the existing record (jessie rubi): one chart, the duplicate
// retired (never deleted), hidden from lists, read-only, and a computer that was offline meanwhile cannot write to it (its changes land on the kept record).
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class PatientMergeGav12Tests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<Guid> NewPatientAsync(string first, string last, string? address = null, string? contact = null) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Patients.Add(new Patient
        {
            PatientId = id, PatientCode = "G12-" + id.ToString("N")[..9], FirstName = first, LastName = last + id.ToString("N")[..4], DateOfBirth = new DateOnly(1990, 1, 2),
            Sex = SexType.Male, Email = "", Address = address, ContactNumber = contact, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    });

    private async Task<(Guid Bid, Guid Cid)> VisitAsync(Guid patientId, BookingStatus status, bool finished, DateOnly? day = null) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var bid = Guid.NewGuid(); var cid = Guid.NewGuid();
        db.Bookings.Add(new Booking
        {
            BookingId = bid, PatientId = patientId, DoctorId = W.DoctorA.RowId, AppointmentDate = day ?? ClinicApp.Domain.ClinicClock.Today, Status = status, PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = "M-" + bid.ToString("N")[..4], VisitType = VisitType.New, ConsultationFeeSnapshot = 450, TotalFee = 450, AmountDue = 450, IsWalkIn = true, CreatedAt = now, UpdatedAt = now,
        });
        db.Consultations.Add(new Consultation
        {
            ConsultationId = cid, BookingId = bid, PatientId = patientId, DoctorId = W.DoctorA.RowId, Status = finished ? ConsultationStatus.Completed : ConsultationStatus.Draft,
            ChiefComplaint = "Fake fever", CompletedAt = finished ? now : null, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (bid, cid);
    });

    private static object Body(Guid keep, Guid merge) => new { keep_patient_id = keep, merge_patient_id = merge, note = "Same person, registered twice at the desk" };

    [Fact]
    public async Task Doctor_merges_the_duplicate_into_the_existing_patient_while_consulting_it()
    {
        var jessie = await NewPatientAsync("Jessie", "Rubi", address: null, contact: "0917-000-0001");
        var jese = await NewPatientAsync("Jese", "Rubi", address: "Fake St 1", contact: "0999-000-0002");
        var old = await VisitAsync(jessie, BookingStatus.Completed, finished: true, day: ClinicApp.Domain.ClinicClock.Today.AddDays(-3));
        var today = await VisitAsync(jese, BookingStatus.InProgress, finished: false);

        using var doc = F.As(W.DoctorA, "Doctor");
        var pre = await doc.GetAsync($"/api/patients/merge-preview?keep={jessie}&merge={jese}");
        await ShouldBe(HttpStatusCode.OK, pre);
        var pj = await JsonOf(pre);
        Assert.Equal(1, pj.GetProperty("merge_active_today").GetArrayLength());      // the visit being consulted: a warning, not a stop
        Assert.Equal(0, pj.GetProperty("keep_active_today").GetArrayLength());

        var res = await doc.PostAsync("/api/patients/merge", ApiFactory.Body(Body(jessie, jese)));
        await ShouldBe(HttpStatusCode.OK, res);
        var json = await JsonOf(res);
        Assert.Equal(jessie, json.GetProperty("kept_patient_id").GetGuid());

        await WithDb(async db =>
        {
            Assert.Equal(jessie, (await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == today.Bid)).PatientId);   // the open visit moved
            Assert.Equal(jessie, (await db.Consultations.AsNoTracking().SingleAsync(c => c.ConsultationId == today.Cid)).PatientId);
            Assert.Equal(jessie, (await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == old.Bid)).PatientId);
            var k = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == jessie);
            Assert.Equal("Fake St 1", k.Address); Assert.Equal("0917-000-0001", k.ContactNumber);   // keeper wins, blanks filled
            var m = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == jese);
            Assert.Equal(jessie, m.MergedIntoPatientId); Assert.NotNull(m.DeletedAt);             // retired, still there
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == jese && a.Action == "Merged into another patient"));
            return 0;
        });
    }

    [Fact]
    public async Task A_merged_away_patient_is_hidden_from_lists_read_only_and_cannot_be_booked_and_its_link_redirects()
    {
        var keep = await NewPatientAsync("Hidekeep", "Zqx");
        var gone = await NewPatientAsync("Hidegone", "Zqx");
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));

        var search = await JsonOf(await doc.GetAsync("/api/patients/search?q=Hidegone"));
        Assert.Equal(0, search.GetProperty("totalCount").GetInt32());
        var all = await JsonOf(await doc.GetAsync("/api/patients?search=Hidegone"));
        Assert.Equal(0, all.GetArrayLength());
        var global = await JsonOf(await doc.GetAsync("/api/search?q=Hidegone"));
        Assert.Equal(0, global.GetProperty("patients").GetArrayLength());

        var get = await JsonOf(await doc.GetAsync($"/api/patients/{gone}"));                 // the old link still opens, and says where to go
        Assert.Equal(keep, get.GetProperty("merged_into_patient_id").GetGuid());
        var redirect = await JsonOf(await doc.GetAsync($"/api/patients/{gone}/merged-into"));
        Assert.Equal(keep, redirect.GetProperty("merged_into_patient_id").GetGuid());

        var put = await doc.PutAsync($"/api/patients/{gone}", ApiFactory.Body(new { first_name = "X", last_name = "Y", date_of_birth = "1990-01-02", sex = "Male" }));
        await ShouldBe(HttpStatusCode.Conflict, put);
        Assert.Equal("patient_merged", (await JsonOf(put)).GetProperty("error").GetString());
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/patients/{gone}/allergies", ApiFactory.Body(Array.Empty<object>())));
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.Conflict, await staff.PostAsync("/api/queue", ApiFactory.Body(new { patient_id = gone })));
    }

    [Fact]
    public async Task Admin_can_unmerge_the_record_comes_back_and_its_visits_stay_on_the_kept_patient_doctor_cannot()
    {
        var keep = await NewPatientAsync("Unkeep", "Zqu");
        var gone = await NewPatientAsync("Ungone", "Zqu");
        var v = await VisitAsync(gone, BookingStatus.Completed, finished: true);
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));
        await ShouldBe(HttpStatusCode.Forbidden, await doc.PostAsync($"/api/patients/{gone}/unmerge", ApiFactory.Body(new { note = "Wrong merge, different people" })));

        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync($"/api/patients/{gone}/unmerge", ApiFactory.Body(new { note = "no" })));
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/patients/{gone}/unmerge", ApiFactory.Body(new { note = "Wrong merge, different people" })));
        await ShouldBe(HttpStatusCode.Conflict, await admin.PostAsync($"/api/patients/{gone}/unmerge", ApiFactory.Body(new { note = "Wrong merge, different people" })));
        await WithDb(async db =>
        {
            var p = await db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == gone);
            Assert.Null(p.MergedIntoPatientId); Assert.Null(p.DeletedAt);
            Assert.Equal(keep, (await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == v.Bid)).PatientId);   // moved back only with "Wrong patient?"
            return 0;
        });
        var search = await JsonOf(await doc.GetAsync("/api/patients/search?q=Ungone"));
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Merging_a_patient_that_others_were_merged_into_repoints_them_and_nkda_gives_way_to_a_real_allergy()
    {
        var a = await NewPatientAsync("Chaina", "Zqc"); var b = await NewPatientAsync("Chainb", "Zqc"); var c = await NewPatientAsync("Chainc", "Zqc");
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.PatientAllergies.AddRange(
                new PatientAllergy { Id = Guid.NewGuid(), PatientId = c, Allergen = "", IsNkda = true, CreatedAt = now, UpdatedAt = now },
                new PatientAllergy { Id = Guid.NewGuid(), PatientId = b, Allergen = "Sulfa", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(); return 0;
        });
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(b, a))));
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(c, b))));
        await WithDb(async db =>
        {
            Assert.Equal(c, (await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == a)).MergedIntoPatientId);
            var al = await db.PatientAllergies.AsNoTracking().Where(x => x.PatientId == c).ToListAsync();
            Assert.Single(al); Assert.Equal("Sulfa", al[0].Allergen); Assert.False(al[0].IsNkda);
            return 0;
        });
    }

    // ── sync: a computer that was offline during the merge ──
    private async Task<HttpClient> NodeAsync()
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
        return client;
    }

    private static object Op(string aggregate, Guid entityId, object data, string type, long? baseRv) => new Dictionary<string, object?>
    {
        ["operation_id"] = Guid.NewGuid(), ["aggregate"] = aggregate, ["entity_id"] = entityId, ["operation_type"] = type, ["base_row_version"] = baseRv,
        ["created_at"] = DateTimeOffset.UtcNow, ["payload"] = new Dictionary<string, object?> { ["data"] = data, ["changed_fields"] = null },
    };

    private async Task<JsonElement> PushOneAsync(HttpClient node, object op)
    {
        var res = await node.PostAsync("/api/sync/push", new StringContent(JsonSerializer.Serialize(new { operations = new[] { op } }), Encoding.UTF8, "application/json"));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("results")[0];
    }

    [Fact]
    public async Task Offline_new_visit_for_a_merged_away_patient_lands_on_the_kept_patient()
    {
        var keep = await NewPatientAsync("Offkeep", "Zqo"); var gone = await NewPatientAsync("Offgone", "Zqo");
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));
        using var node = await NodeAsync();
        var bid = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var row = new Dictionary<string, object?>
        {
            ["booking_id"] = bid, ["patient_id"] = gone, ["doctor_id"] = W.DoctorA.RowId, ["appointment_date"] = ClinicApp.Domain.ClinicClock.Today.ToString("yyyy-MM-dd"),
            ["slot_start_time"] = "00:00:00", ["slot_end_time"] = "00:00:00", ["status"] = "CheckedIn", ["payment_mode"] = "PayAtClinic", ["queue_number"] = "Z-" + bid.ToString("N")[..5],
            ["consultation_fee_snapshot"] = 450, ["total_fee"] = 450, ["amount_due"] = 450, ["is_walk_in"] = true, ["visit_type"] = "New", ["created_at"] = now, ["updated_at"] = now,
        };
        var r = await PushOneAsync(node, Op("booking", bid, new Dictionary<string, object?> { ["booking"] = row }, "create", null));
        Assert.Equal("merged", r.GetProperty("status").GetString());
        Assert.Equal("patient_reassigned", r.GetProperty("reason_code").GetString());
        Assert.Equal(keep, r.GetProperty("current").GetProperty("booking").GetProperty("patient_id").GetGuid());   // the computer updates its visit
        Assert.Equal(keep, await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == bid).Select(b => b.PatientId).SingleAsync()));
    }

    [Fact]
    public async Task Offline_edit_and_allergy_on_a_merged_away_patient_go_to_the_kept_record_blanks_only()
    {
        var keep = await NewPatientAsync("Edkeep", "Zqe", address: null, contact: "0917-KEEP");
        var gone = await NewPatientAsync("Edgone", "Zqe");
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));
        var goneRow = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == gone));
        using var node = await NodeAsync();
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var data = new Dictionary<string, object?>
        {
            ["patient_id"] = gone, ["patient_code"] = goneRow.PatientCode, ["first_name"] = "Edgone", ["last_name"] = goneRow.LastName, ["date_of_birth"] = "1990-01-02", ["sex"] = "Male",
            ["email"] = "", ["address"] = "Offline Address 9", ["contact_number"] = "0999-OFFLINE", ["created_at"] = goneRow.CreatedAt, ["updated_at"] = now,
            ["allergies"] = new[] { new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["patient_id"] = gone, ["allergen"] = "Aspirin", ["is_nkda"] = false, ["created_at"] = now, ["updated_at"] = now } },
        };
        var r = await PushOneAsync(node, Op("patient", gone, data, "update", 1));
        Assert.Equal("merged", r.GetProperty("status").GetString());
        Assert.Equal("patient_merged", r.GetProperty("reason_code").GetString());
        await WithDb(async db =>
        {
            var k = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == keep);
            Assert.Equal("Offline Address 9", k.Address);       // blank on the kept record: filled
            Assert.Equal("0917-KEEP", k.ContactNumber);          // never overwritten
            Assert.Contains(await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == keep).Select(a => a.Allergen).ToListAsync(), a => a == "Aspirin");
            var g = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == gone);
            Assert.Null(g.Address); Assert.NotNull(g.DeletedAt);   // the retired record is not written
            return 0;
        });
    }

    [Fact]
    public async Task An_amendment_from_a_computer_that_still_has_the_merged_away_patient_is_applied()
    {
        var keep = await NewPatientAsync("Amkeep", "Zqa"); var gone = await NewPatientAsync("Amgone", "Zqa");
        var v = await VisitAsync(gone, BookingStatus.Completed, finished: true);
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));
        var crv = await WithDb(db => db.Consultations.AsNoTracking().Where(c => c.ConsultationId == v.Cid).Select(c => c.RowVersion).SingleAsync());
        using var node = await NodeAsync();
        var r = await PushOneAsync(node, Op("consultation", v.Cid, new Dictionary<string, object?>
        {
            ["consultation"] = new Dictionary<string, object?>
            {
                ["consultation_id"] = v.Cid, ["booking_id"] = v.Bid, ["patient_id"] = gone, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Amended",
                ["chief_complaint"] = "Amended after merge", ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
            },
        }, "update", crv));
        Assert.NotEqual("rejected", r.GetProperty("status").GetString());
        var c = await WithDb(db => db.Consultations.AsNoTracking().SingleAsync(x => x.ConsultationId == v.Cid));
        Assert.Equal(keep, c.PatientId); Assert.Equal("Amended after merge", c.ChiefComplaint);
    }

    [Fact]
    public async Task The_pull_sends_the_merged_away_patient_with_its_kept_record_so_desktops_can_remap()
    {
        var keep = await NewPatientAsync("Pullkeep", "Zqp"); var gone = await NewPatientAsync("Pullgone", "Zqp");
        using var node = await NodeAsync();
        var first = await JsonOf(await node.GetAsync("/api/sync/pull?limit=1000"));
        var cursor = first.GetProperty("next_cursor").GetString();
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));
        string? found = null; var more = true;
        for (var i = 0; i < 20 && more && found is null; i++)
        {
            var page = await JsonOf(await node.GetAsync($"/api/sync/pull?since={cursor}&limit=1000&settle_ms=0"));
            foreach (var ch in page.GetProperty("changes").EnumerateArray())
                if (ch.GetProperty("aggregate").GetString() == "patient" && ch.GetProperty("entity_id").GetString() == gone.ToString())
                    found = ch.GetProperty("operation").GetString() == "delete" ? "delete"
                        : ch.GetProperty("data").GetProperty("merged_into_patient_id").GetString();
            cursor = page.GetProperty("next_cursor").GetString(); more = page.GetProperty("has_more").GetBoolean();
        }
        Assert.Equal(keep.ToString(), found);
    }

    //JJQR - AI (2026-11-26) - GAV-12 review 1.1 + 3.1: a resend after a lost reply answers OK (already), and the front desk can read merged-into but not merge
    [Fact]
    public async Task A_merge_resend_answers_ok_already_and_staff_can_read_merged_into_but_not_merge()
    {
        var keep = await NewPatientAsync("Resendkeep", "Zqr");
        var gone = await NewPatientAsync("Resendgone", "Zqr");
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.OK, await doc.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone))));
        var again = await doc.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, gone)));
        await ShouldBe(HttpStatusCode.OK, again);
        var aj = await JsonOf(again);
        Assert.True(aj.GetProperty("already").GetBoolean());
        Assert.Equal(keep, aj.GetProperty("kept_patient_id").GetGuid());
        var other = await NewPatientAsync("Resendother", "Zqr");                              // the same record into a DIFFERENT patient is still refused
        await ShouldBe(HttpStatusCode.Conflict, await doc.PostAsync("/api/patients/merge", ApiFactory.Body(Body(other, gone))));

        using var staff = F.As(W.Staff, "Staff");
        var mi = await staff.GetAsync($"/api/patients/{gone}/merged-into");
        await ShouldBe(HttpStatusCode.OK, mi);
        Assert.Equal(keep, (await JsonOf(mi)).GetProperty("merged_into_patient_id").GetGuid());
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, other))));
        await ShouldBe(HttpStatusCode.Forbidden, await staff.GetAsync($"/api/patients/merge-preview?keep={keep}&merge={other}"));
    }
    //JJQR - END
}
//JJQR - END
