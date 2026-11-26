//JJQR - AI (2026-11-26) - GAV-11 why: "Wrong patient?" — the doctor (or an admin) moves ONE visit to a new or existing patient; sync can never move it back
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class BookingReassignTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<Guid> NewPatientAsync(string first, DateOnly? dob = null) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Patients.Add(new Patient
        {
            PatientId = id, PatientCode = "RA-" + id.ToString("N")[..10], FirstName = first, LastName = "Fake" + id.ToString("N")[..5], DateOfBirth = dob ?? new DateOnly(1980, 3, 3),
            Sex = SexType.Male, Email = "", CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    });

    /// <summary>One visit with everything a visit can hold. status = booking status; finished = consultation Completed.</summary>
    private async Task<(Guid Bid, Guid Cid, Guid Pay)> VisitAsync(Guid patientId, BookingStatus status, bool finished, bool paid = false, DateOnly? date = null) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var bid = Guid.NewGuid(); var cid = Guid.NewGuid(); var pay = Guid.NewGuid();
        var day = date ?? ClinicApp.Domain.ClinicClock.Today;
        db.Bookings.Add(new Booking
        {
            BookingId = bid, PatientId = patientId, DoctorId = W.DoctorA.RowId, AppointmentDate = day, Status = status, PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = "R-" + bid.ToString("N")[..4], VisitType = VisitType.FollowUp, ConsultationFeeSnapshot = 450, TotalFee = 450, AmountDue = 450, IsWalkIn = true,
            CreatedAt = now.AddMinutes(-30), UpdatedAt = now,
        });
        db.Payments.Add(new Payment
        {
            PaymentId = pay, BookingId = bid, Amount = 450, Status = paid ? PaymentStatus.Paid : PaymentStatus.Unpaid, OrNumber = paid ? "OR-" + bid.ToString("N")[..6] : null,
            PaymentMethod = paid ? PaymentMethod.Cash : null, AmountReceived = paid ? 450 : null, ConfirmedAt = paid ? now : null, CreatedAt = now, UpdatedAt = now,
        });
        db.Consultations.Add(new Consultation
        {
            ConsultationId = cid, BookingId = bid, PatientId = patientId, DoctorId = W.DoctorA.RowId, Status = finished ? ConsultationStatus.Completed : ConsultationStatus.Draft,
            ChiefComplaint = "Fake cough", CompletedAt = finished ? now : null, CreatedAt = now, UpdatedAt = now,
        });
        db.ConsultationDiagnoses.Add(new ConsultationDiagnosis { Id = Guid.NewGuid(), ConsultationId = cid, Icd10Code = "J06.9", Type = DiagnosisType.Primary, CreatedAt = now });
        db.LabOrders.Add(new LabOrder { LabOrderId = Guid.NewGuid(), ConsultationId = cid, PatientId = patientId, DoctorId = W.DoctorA.RowId, TestName = "CBC", RequestedAt = now, CreatedAt = now, UpdatedAt = now });
        db.MedicalCertificates.Add(new MedicalCertificate { CertificateId = Guid.NewGuid(), ConsultationId = cid, PatientId = patientId, DoctorId = W.DoctorA.RowId, IssueDate = day, CreatedAt = now, UpdatedAt = now });
        db.FollowUps.Add(new FollowUp { Id = Guid.NewGuid(), ConsultationId = cid, PatientId = patientId, DoctorId = W.DoctorA.RowId, FollowUpDate = day.AddDays(7), CreatedAt = now, UpdatedAt = now });
        db.PrescriptionGroups.Add(new PrescriptionGroup { GroupId = Guid.NewGuid(), BookingId = bid, PatientId = patientId, DoctorId = W.DoctorA.RowId, CreatedAt = now, UpdatedAt = now });
        var template = await db.VitalFieldTemplates.Select(t => t.TemplateId).FirstAsync();
        db.PatientVitalReadings.Add(new PatientVitalReading { Id = Guid.NewGuid(), BookingId = bid, PatientId = patientId, TemplateId = template, Value = "37.0", RecordedAt = day, CreatedAt = now, UpdatedAt = now });
        db.PatientDocuments.Add(new PatientDocument { Id = Guid.NewGuid(), PatientId = patientId, BookingId = bid, ConsultationId = cid, FileName = "fake.pdf", FileUrl = $"/uploads/{patientId}/{bid}/fake.pdf", UploadedAt = now });
        db.PatientLabResults.Add(new PatientLabResult { Id = Guid.NewGuid(), PatientId = patientId, BookingId = bid, ConsultationId = cid, FileName = "lab.pdf", FileUrl = $"/uploads/{patientId}/{bid}/lab.pdf", UploadedAt = now });
        await db.SaveChangesAsync();
        return (bid, cid, pay);
    });

    private static object NewBody(string first = "Jessie", string last = "Fakerubi", string dob = "1999-09-09", List<Guid>? allergies = null, bool ackDup = false, Guid? id = null) => new
    {
        mode = "new", reason = "Different person, staff picked the wrong record",
        new_patient = new { patient_id = id ?? Guid.Empty, first_name = first, last_name = last, date_of_birth = dob, sex = "Male", email = "" },
        move_allergy_ids = allergies, acknowledge_possible_duplicate = ackDup,
    };

    private static object ExistingBody(Guid target, bool ack = false, List<Guid>? allergies = null) => new
    {
        mode = "existing", target_patient_id = target, reason = "This visit belongs to the other patient", acknowledge_same_day_visit = ack, move_allergy_ids = allergies,
    };

    private Task<HttpResponseMessage> PostAsync(HttpClient c, Guid bid, object body) => c.PostAsync($"/api/bookings/{bid}/reassign-patient", ApiFactory.Body(body));

    private Task<int> CountForAsync(Guid pid, Guid bid) => WithDb(async db =>
        await db.Consultations.CountAsync(x => x.PatientId == pid && x.BookingId == bid)
        + await db.LabOrders.CountAsync(x => x.PatientId == pid && db.Consultations.Any(c => c.ConsultationId == x.ConsultationId && c.BookingId == bid))
        + await db.MedicalCertificates.CountAsync(x => x.PatientId == pid && db.Consultations.Any(c => c.ConsultationId == x.ConsultationId && c.BookingId == bid))
        + await db.FollowUps.CountAsync(x => x.PatientId == pid && db.Consultations.Any(c => c.ConsultationId == x.ConsultationId && c.BookingId == bid))
        + await db.PrescriptionGroups.CountAsync(x => x.PatientId == pid && x.BookingId == bid) + await db.PatientVitalReadings.CountAsync(x => x.PatientId == pid && x.BookingId == bid)
        + await db.PatientDocuments.CountAsync(x => x.PatientId == pid && x.BookingId == bid) + await db.PatientLabResults.CountAsync(x => x.PatientId == pid && x.BookingId == bid));

    [Fact]
    public async Task Open_visit_moves_to_a_new_patient_with_everything_and_the_old_visits_stay()
    {
        var p1 = await NewPatientAsync("Personone");
        var past = await VisitAsync(p1, BookingStatus.Completed, finished: true, paid: true, date: ClinicApp.Domain.ClinicClock.Today.AddDays(-20));
        var today = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        var rvBefore = await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == today.Bid).Select(b => b.RowVersion).SingleAsync());

        using var doc = F.As(W.DoctorA, "Doctor");
        var res = await PostAsync(doc, today.Bid, NewBody());
        await ShouldBe(HttpStatusCode.OK, res);
        var json = await JsonOf(res);
        var p2 = json.GetProperty("to_patient_id").GetGuid();
        Assert.StartsWith("MF-", json.GetProperty("patient_code").GetString());
        Assert.Equal(1, json.GetProperty("moved").GetProperty("consultations").GetInt32());

        await WithDb(async db =>
        {
            var b = await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == today.Bid);
            Assert.Equal(p2, b.PatientId);
            Assert.Equal(VisitType.New, b.VisitType);                         // an open visit of a NEW person is a new-patient visit
            Assert.True(b.RowVersion > rvBefore);                              // desktops receive it
            Assert.Equal(today.Pay, (await db.Payments.AsNoTracking().SingleAsync(x => x.BookingId == today.Bid)).PaymentId);
            Assert.Equal(p1, (await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == past.Bid)).PatientId);   // the other visit never moves
            Assert.Equal(p1, (await db.Consultations.AsNoTracking().SingleAsync(x => x.BookingId == past.Bid)).PatientId);
            var p1Row = await db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == p1);
            Assert.Equal("Personone", p1Row.FirstName); Assert.Null(p1Row.DeletedAt);                                  // person 1 untouched
            var newRow = await db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == p2);
            Assert.Equal("Jessie", newRow.FirstName); Assert.Equal(new DateOnly(1999, 9, 9), newRow.DateOfBirth);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == p1 && a.Action == "Visit moved to another patient"));
            var inAudit = await db.AuditLogs.SingleAsync(a => a.EntityId == p2 && a.Action == "Visit moved in from another patient");
            Assert.DoesNotContain("Personone", inAudit.Details); Assert.Contains(today.Bid.ToString(), inAudit.Details);   // ids / codes, no names
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == today.Bid && a.Action == "Wrong patient fixed"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == today.Cid && a.Action == "Wrong patient fixed"));
            return 0;
        });
        Assert.Equal(8, await CountForAsync(p2, today.Bid));      // consultation, lab order, certificate, follow-up, prescription, vital, document, lab result
        Assert.Equal(0, await CountForAsync(p1, today.Bid));
    }

    [Fact]
    public async Task Completed_and_paid_visit_moves_to_an_existing_patient_with_its_payment_and_OR()
    {
        var p1 = await NewPatientAsync("Paidone"); var p2 = await NewPatientAsync("Paidtwo");
        var v = await VisitAsync(p1, BookingStatus.Completed, finished: true, paid: true);
        var or = await WithDb(db => db.Payments.AsNoTracking().Where(x => x.PaymentId == v.Pay).Select(x => x.OrNumber).SingleAsync());

        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await PostAsync(admin, v.Bid, ExistingBody(p2)));
        await WithDb(async db =>
        {
            var b = await db.Bookings.AsNoTracking().Include(x => x.Payment).SingleAsync(x => x.BookingId == v.Bid);
            Assert.Equal(p2, b.PatientId);
            Assert.Equal(VisitType.FollowUp, b.VisitType);                    // a finished, billed visit keeps its fee record as printed
            Assert.Equal(450, b.TotalFee);
            Assert.Equal(PaymentStatus.Paid, b.Payment!.Status); Assert.Equal(or, b.Payment.OrNumber);
            Assert.Equal(ConsultationStatus.Completed, (await db.Consultations.AsNoTracking().SingleAsync(x => x.ConsultationId == v.Cid)).Status);
            return 0;
        });
        Assert.Equal(8, await CountForAsync(p2, v.Bid));
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Patient")]
    [InlineData("DoctorB")]
    public async Task Staff_patients_and_another_doctor_are_refused_and_nothing_moves(string who)
    {
        var p1 = await NewPatientAsync("Roleone");
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        using var c = who switch { "Staff" => F.As(W.Staff, "Staff"), "DoctorB" => F.As(W.DoctorB, "Doctor"), _ => F.Patient(W.PatientA) };
        await ShouldBe(HttpStatusCode.Forbidden, await PostAsync(c, v.Bid, NewBody()));
        await ShouldBe(HttpStatusCode.Forbidden, await c.GetAsync($"/api/bookings/{v.Bid}/reassign-preview"));
        Assert.Equal(p1, await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == v.Bid).Select(b => b.PatientId).SingleAsync()));
    }

    [Fact]
    public async Task Target_with_a_visit_that_day_needs_the_warning_acknowledged_first()
    {
        var p1 = await NewPatientAsync("Warnone"); var p2 = await NewPatientAsync("Warntwo");
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        var other = await VisitAsync(p2, BookingStatus.CheckedIn, finished: false);
        using var doc = F.As(W.DoctorA, "Doctor");

        var pre = await JsonOf(await doc.GetAsync($"/api/bookings/{v.Bid}/reassign-preview?target={p2}"));
        Assert.True(pre.GetProperty("target").GetProperty("has_visit_that_day").GetBoolean());

        var warn = await PostAsync(doc, v.Bid, ExistingBody(p2));
        await ShouldBe(HttpStatusCode.Conflict, warn);
        var w = await JsonOf(warn);
        Assert.Equal("target_has_visit_that_day", w.GetProperty("error").GetString());
        Assert.Equal(other.Bid, w.GetProperty("visits")[0].GetProperty("booking_id").GetGuid());
        Assert.Equal(p1, await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == v.Bid).Select(b => b.PatientId).SingleAsync()));   // nothing moved yet

        await ShouldBe(HttpStatusCode.OK, await PostAsync(doc, v.Bid, ExistingBody(p2, ack: true)));
        Assert.Equal(p2, await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == v.Bid).Select(b => b.PatientId).SingleAsync()));
    }

    [Fact]
    public async Task New_patient_with_the_same_name_and_birthday_as_someone_on_file_is_a_warning_first()
    {
        var p1 = await NewPatientAsync("Dupone");
        var twin = await NewPatientAsync("Twinperson", new DateOnly(1977, 7, 7));
        var twinLast = await WithDb(db => db.Patients.Where(p => p.PatientId == twin).Select(p => p.LastName).SingleAsync());
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        using var doc = F.As(W.DoctorA, "Doctor");
        var res = await PostAsync(doc, v.Bid, NewBody("twin person", twinLast, "1977-07-07"));
        await ShouldBe(HttpStatusCode.Conflict, res);
        Assert.Equal("possible_duplicate", (await JsonOf(res)).GetProperty("error").GetString());
        await ShouldBe(HttpStatusCode.OK, await PostAsync(doc, v.Bid, NewBody("Twin Person", twinLast, "1977-07-07", ackDup: true)));
    }

    private async Task<(Guid Old, Guid Today)> AllergiesAsync(Guid pid) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow; var o = Guid.NewGuid(); var t = Guid.NewGuid();
        db.PatientAllergies.AddRange(
            new PatientAllergy { Id = o, PatientId = pid, Allergen = "Penicillin", CreatedAt = now.AddDays(-30), UpdatedAt = now.AddDays(-30) },   // person 1's own
            new PatientAllergy { Id = t, PatientId = pid, Allergen = "Latex", CreatedAt = now, UpdatedAt = now });                                 // recorded during this visit
        await db.SaveChangesAsync();
        return (o, t);
    });

    [Fact]
    public async Task Allergies_added_during_the_visit_move_by_default_and_stay_when_unticked()
    {
        using var doc = F.As(W.DoctorA, "Doctor");
        // default (ticked): only the one added during the visit moves
        var p1 = await NewPatientAsync("Allone");
        var (old, added) = await AllergiesAsync(p1);
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        var pre = await JsonOf(await doc.GetAsync($"/api/bookings/{v.Bid}/reassign-preview"));
        var flags = pre.GetProperty("allergies").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetGuid(), a => a.GetProperty("added_during_visit").GetBoolean());
        Assert.True(flags[added]); Assert.False(flags[old]);
        var res = await JsonOf(await PostAsync(doc, v.Bid, NewBody(last: "Allergyon")));
        var p2 = res.GetProperty("to_patient_id").GetGuid();
        Assert.Equal(1, res.GetProperty("allergies_moved").GetInt32());
        Assert.Equal(new[] { "Latex" }, await WithDb(db => db.PatientAllergies.Where(a => a.PatientId == p2).Select(a => a.Allergen).ToListAsync()));
        Assert.Equal(new[] { "Penicillin" }, await WithDb(db => db.PatientAllergies.Where(a => a.PatientId == p1).Select(a => a.Allergen).ToListAsync()));

        // unticked (empty list): nothing moves
        var q1 = await NewPatientAsync("Alltwo");
        await AllergiesAsync(q1);
        var v2 = await VisitAsync(q1, BookingStatus.InProgress, finished: false);
        var res2 = await JsonOf(await PostAsync(doc, v2.Bid, NewBody(last: "Allergyoff", allergies: new List<Guid>())));
        var q2 = res2.GetProperty("to_patient_id").GetGuid();
        Assert.Equal(0, await WithDb(db => db.PatientAllergies.CountAsync(a => a.PatientId == q2)));
        Assert.Equal(2, await WithDb(db => db.PatientAllergies.CountAsync(a => a.PatientId == q1)));
    }

    [Fact]
    public async Task Saving_the_allergy_list_keeps_the_first_created_date_of_an_allergy_already_on_file()
    {
        var p1 = await NewPatientAsync("Keepdate");
        var (old, _) = await AllergiesAsync(p1);
        using var doc = F.As(W.DoctorA, "Doctor");
        var res = await doc.PutAsync($"/api/patients/{p1}/allergies", ApiFactory.Body(new object[]
        {
            new { allergen = "Penicillin" }, new { allergen = "Latex" }, new { allergen = "Shrimp" },
        }));
        await ShouldBe(HttpStatusCode.OK, res);
        var rows = await WithDb(db => db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == p1).ToListAsync());
        Assert.True(rows.Single(a => a.Allergen == "Penicillin").CreatedAt < DateTimeOffset.UtcNow.AddDays(-29));   // still 30 days old
        Assert.True(rows.Single(a => a.Allergen == "Shrimp").CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Bad_requests_are_refused_and_change_nothing()
    {
        var p1 = await NewPatientAsync("Badone");
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        using var doc = F.As(W.DoctorA, "Doctor");
        await ShouldBe(HttpStatusCode.BadRequest, await PostAsync(doc, v.Bid, new { mode = "existing", target_patient_id = W.PatientB.RowId, reason = "no" }));
        await ShouldBe(HttpStatusCode.BadRequest, await PostAsync(doc, v.Bid, new { mode = "other", reason = "long enough reason" }));
        await ShouldBe(HttpStatusCode.BadRequest, await PostAsync(doc, v.Bid, NewBody(first: "")));
        await ShouldBe(HttpStatusCode.NotFound, await PostAsync(doc, v.Bid, ExistingBody(Guid.NewGuid())));
        await ShouldBe(HttpStatusCode.NotFound, await PostAsync(doc, Guid.NewGuid(), ExistingBody(W.PatientB.RowId)));
        Assert.Equal(p1, await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == v.Bid).Select(b => b.PatientId).SingleAsync()));
    }

    [Fact]
    public async Task A_retry_of_a_lost_reply_answers_ok_without_moving_twice()
    {
        var p1 = await NewPatientAsync("Retryone");
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        using var doc = F.As(W.DoctorA, "Doctor");
        var id = Guid.NewGuid();
        await ShouldBe(HttpStatusCode.OK, await PostAsync(doc, v.Bid, NewBody(last: "Retrytwo", id: id)));
        var again = await PostAsync(doc, v.Bid, NewBody(last: "Retrytwo", id: id));
        await ShouldBe(HttpStatusCode.OK, again);
        Assert.True((await JsonOf(again)).GetProperty("already").GetBoolean());
        Assert.Equal(1, await WithDb(db => db.Patients.CountAsync(p => p.PatientId == id)));
    }

    // ── sync: a computer that never saw the move cannot put the visit back on the first patient ──
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
    public async Task Sync_cannot_push_the_visit_back_onto_the_old_patient()
    {
        var p1 = await NewPatientAsync("Syncone");
        var v = await VisitAsync(p1, BookingStatus.InProgress, finished: false);
        var oldRv = await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == v.Bid).Select(b => b.RowVersion).SingleAsync());
        var crv = await WithDb(db => db.Consultations.AsNoTracking().Where(c => c.ConsultationId == v.Cid).Select(c => c.RowVersion).SingleAsync());
        using var doc = F.As(W.DoctorA, "Doctor");
        var p2 = (await JsonOf(await PostAsync(doc, v.Bid, NewBody(last: "Syncmoved")))).GetProperty("to_patient_id").GetGuid();
        using var node = await NodeAsync();
        var now = DateTimeOffset.UtcNow.AddMinutes(5); // the computer's copy even claims to be newer

        // 1. stale booking (old patient, old row version)
        var booking = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == v.Bid));
        var bRow = new Dictionary<string, object?>
        {
            ["booking_id"] = v.Bid, ["patient_id"] = p1, ["doctor_id"] = W.DoctorA.RowId, ["appointment_date"] = booking.AppointmentDate.ToString("yyyy-MM-dd"),
            ["slot_start_time"] = "00:00:00", ["slot_end_time"] = "00:00:00", ["status"] = "InProgress", ["payment_mode"] = "PayAtClinic", ["queue_number"] = booking.QueueNumber,
            ["consultation_fee_snapshot"] = 450, ["total_fee"] = 450, ["amount_due"] = 450, ["is_walk_in"] = true, ["visit_type"] = "FollowUp", ["notes"] = "node note",
            ["created_at"] = booking.CreatedAt, ["updated_at"] = now,
        };
        var rb = await PushOneAsync(node, Op("booking", v.Bid, new Dictionary<string, object?> { ["booking"] = bRow }, "update", oldRv));
        Assert.Equal("merged", rb.GetProperty("status").GetString());
        Assert.Equal("patient_reassigned", rb.GetProperty("reason_code").GetString());

        // 2. stale consultation draft with children carrying the old patient
        var fuId = await WithDb(db => db.FollowUps.Where(f => f.ConsultationId == v.Cid).Select(f => f.Id).SingleAsync());
        var cDoc = new Dictionary<string, object?>
        {
            ["consultation"] = new Dictionary<string, object?>
            {
                ["consultation_id"] = v.Cid, ["booking_id"] = v.Bid, ["patient_id"] = p1, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Draft",
                ["chief_complaint"] = "Edited offline", ["created_at"] = now, ["updated_at"] = now,
            },
            ["follow_up"] = new Dictionary<string, object?>
            {
                ["id"] = fuId, ["consultation_id"] = v.Cid, ["patient_id"] = p1, ["doctor_id"] = W.DoctorA.RowId,
                ["follow_up_date"] = ClinicApp.Domain.ClinicClock.Today.AddDays(14).ToString("yyyy-MM-dd"), ["status"] = "Pending", ["created_at"] = now, ["updated_at"] = now,
            },
            ["lab_orders"] = new[] { new Dictionary<string, object?> { ["lab_order_id"] = Guid.NewGuid(), ["consultation_id"] = v.Cid, ["patient_id"] = p1, ["doctor_id"] = W.DoctorA.RowId, ["test_name"] = "FBS", ["status"] = "Requested", ["requested_at"] = now, ["created_at"] = now, ["updated_at"] = now } },
        };
        var rc = await PushOneAsync(node, Op("consultation", v.Cid, cDoc, "update", crv));
        Assert.Equal("merged", rc.GetProperty("status").GetString());
        Assert.Equal(p2, rc.GetProperty("current").GetProperty("consultation").GetProperty("patient_id").GetGuid());   // the computer is told the cloud's patient

        // 3. a vital recorded on the computer before it heard about the move
        var template = await WithDb(db => db.VitalFieldTemplates.Select(t => t.TemplateId).Skip(1).FirstAsync());
        var vid = Guid.NewGuid();
        var rv = await PushOneAsync(node, Op("vital_reading", vid, new Dictionary<string, object?>
        {
            ["id"] = vid, ["booking_id"] = v.Bid, ["patient_id"] = p1, ["template_id"] = template, ["value"] = "120/80",
            ["recorded_at"] = ClinicApp.Domain.ClinicClock.Today.ToString("yyyy-MM-dd"), ["created_at"] = now, ["updated_at"] = now,
        }, "create", null));
        Assert.Equal("applied", rv.GetProperty("status").GetString());

        await WithDb(async db =>
        {
            var b = await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == v.Bid);
            Assert.Equal(p2, b.PatientId);
            var c = await db.Consultations.AsNoTracking().SingleAsync(x => x.ConsultationId == v.Cid);
            Assert.Equal(p2, c.PatientId); Assert.Equal("Edited offline", c.ChiefComplaint);   // the doctor's offline edit still lands
            Assert.Equal(p2, (await db.FollowUps.AsNoTracking().SingleAsync(x => x.ConsultationId == v.Cid)).PatientId);
            Assert.All(await db.LabOrders.AsNoTracking().Where(x => x.ConsultationId == v.Cid).ToListAsync(), l => Assert.Equal(p2, l.PatientId));
            Assert.Equal(p2, (await db.PatientVitalReadings.AsNoTracking().SingleAsync(x => x.Id == vid)).PatientId);
            Assert.Equal(0, await db.Consultations.CountAsync(x => x.PatientId == p1));
            return 0;
        });
    }

    [Fact]
    public async Task An_amendment_from_a_computer_that_still_has_the_old_patient_is_applied_to_the_new_one()
    {
        var p1 = await NewPatientAsync("Amendone");
        var v = await VisitAsync(p1, BookingStatus.Completed, finished: true, paid: true);
        using var doc = F.As(W.DoctorA, "Doctor");
        var p2 = (await JsonOf(await PostAsync(doc, v.Bid, NewBody(last: "Amendmoved")))).GetProperty("to_patient_id").GetGuid();
        var crv = await WithDb(db => db.Consultations.AsNoTracking().Where(c => c.ConsultationId == v.Cid).Select(c => c.RowVersion).SingleAsync());
        using var node = await NodeAsync();
        var r = await PushOneAsync(node, Op("consultation", v.Cid, new Dictionary<string, object?>
        {
            ["consultation"] = new Dictionary<string, object?>
            {
                ["consultation_id"] = v.Cid, ["booking_id"] = v.Bid, ["patient_id"] = p1, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Amended",
                ["chief_complaint"] = "Amended text", ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
            },
        }, "update", crv));
        Assert.NotEqual("rejected", r.GetProperty("status").GetString());
        var c = await WithDb(db => db.Consultations.AsNoTracking().SingleAsync(x => x.ConsultationId == v.Cid));
        Assert.Equal(p2, c.PatientId); Assert.Equal("Amended text", c.ChiefComplaint);
    }

    //JJQR - AI (2026-11-26) - GAV-11 review 2.1: the stale-patient amendment is decided from the visit, not from the audit wording (audit row removed here)
    [Fact]
    public async Task An_amendment_with_the_old_patient_is_still_applied_when_the_audit_row_is_missing()
    {
        var p1 = await NewPatientAsync("Amendnoaudit");
        var v = await VisitAsync(p1, BookingStatus.Completed, finished: true, paid: true);
        using var doc = F.As(W.DoctorA, "Doctor");
        var p2 = (await JsonOf(await PostAsync(doc, v.Bid, NewBody(last: "Amendnoauditmoved")))).GetProperty("to_patient_id").GetGuid();
        await WithDb(async db => { await db.AuditLogs.Where(a => a.Action == "Wrong patient fixed" && (a.EntityId == v.Cid || a.EntityId == v.Bid)).ExecuteDeleteAsync(); return 0; });
        var crv = await WithDb(db => db.Consultations.AsNoTracking().Where(c => c.ConsultationId == v.Cid).Select(c => c.RowVersion).SingleAsync());
        using var node = await NodeAsync();
        var r = await PushOneAsync(node, Op("consultation", v.Cid, new Dictionary<string, object?>
        {
            ["consultation"] = new Dictionary<string, object?>
            {
                ["consultation_id"] = v.Cid, ["booking_id"] = v.Bid, ["patient_id"] = p1, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Amended",
                ["chief_complaint"] = "Amended without audit", ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
            },
        }, "update", crv));
        Assert.NotEqual("rejected", r.GetProperty("status").GetString());
        var c = await WithDb(db => db.Consultations.AsNoTracking().SingleAsync(x => x.ConsultationId == v.Cid));
        Assert.Equal(p2, c.PatientId); Assert.Equal("Amended without audit", c.ChiefComplaint);
    }
    //JJQR - END
}
//JJQR - END
