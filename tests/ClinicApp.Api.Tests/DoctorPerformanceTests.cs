using System.Net;
using System.Text.Json;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 19, 2026) - GET api/doctor/performance: authz (own numbers only), Manila period boundaries (week starts Monday), money rules, zero-filled series, lists, counts
[Collection("api")]
public class DoctorPerformanceTests(ApiFixture api) : ApiTestBase(api)
{
    // Frozen "now": 2026-11-18 17:30Z = Thursday 2026-11-19 01:30 in Manila (UTC is still the 18th — the bug window).
    private const string FrozenUtc = "2026-11-18T17:30:00Z";
    private static readonly DateOnly Today = new(2026, 11, 19);

    private sealed record Doc(Guid UserId, Guid DoctorId);

    private async Task<Doc> NewDoctor() => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var uid = Guid.NewGuid();
        db.Users.Add(new User { Id = uid, Email = $"perf-{uid:N}@test.local", PasswordHash = "x", EmailConfirmed = true, CreatedAt = now });
        db.Profiles.Add(new Profile { Id = uid, Role = UserRole.Doctor, CreatedAt = now });
        var sid = Guid.NewGuid();
        db.StaffAccounts.Add(new StaffAccount { StaffId = sid, UserId = uid, FullName = "Perf Doctor", Email = $"perf-{sid:N}@test.local", Role = StaffRole.Doctor, Status = StaffStatus.Active, InvitedAt = now, CreatedAt = now, UpdatedAt = now });
        db.Doctors.Add(new Doctor { DoctorId = sid, Specialization = "GP", ConsultationFee = 450, SlotDurationMinutes = 15, SlotCapacity = 1, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return new Doc(uid, sid);
    });

    private async Task<Guid> NewPatient(string first = "Pat") => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Patients.Add(new Patient { PatientId = id, PatientCode = "PF-" + id.ToString("N")[..10], //JJQR - AI (Nov 19, 2026) - was "MF-"+hex: a code of 10 pure digits (~1% per patient) became the new MAX for the sqlite code allocator and flaked PatientCodeTests. JJQR - END
             FirstName = first, LastName = "Perf", DateOfBirth = new DateOnly(1990, 1, 1), Sex = SexType.Female, Email = $"{id:N}@test.local", ContactNumber = "0917", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return id;
    });

    private sealed record Visit(
        DateOnly Date, BookingStatus Status = BookingStatus.Completed, VisitType Type = VisitType.New, decimal Fee = 450, decimal Due = 0,
        PaymentStatus Pay = PaymentStatus.Paid, decimal? Received = null, decimal? Refund = null, Guid? Patient = null,
        bool Consult = false, int? ConsultMinutes = null, (string? code, string? text)[]? Diagnoses = null, bool Rx = false, bool Cert = false, bool Lab = false,
        DateOnly? FollowUp = null, FollowUpStatus FollowUpStatus = FollowUpStatus.Pending);

    private async Task Seed(Doc d, params Visit[] visits)
    {
        foreach (var v in visits)
        {
            var patient = v.Patient ?? await NewPatient();
            await WithDb<int>(async db =>
            {
                var now = DateTimeOffset.UtcNow;
                var booking = Guid.NewGuid();
                db.Bookings.Add(new Booking
                {
                    BookingId = booking, PatientId = patient, DoctorId = d.DoctorId, AppointmentDate = v.Date, Status = v.Status,
                    PaymentMode = PaymentMode.PayAtClinic, QueueNumber = "P-" + booking.ToString("N")[..8], VisitType = v.Type,
                    ConsultationFeeSnapshot = v.Fee, TotalFee = v.Fee, AmountDue = v.Due, CreatedAt = now, UpdatedAt = now
                });
                db.Payments.Add(new Payment
                {
                    PaymentId = Guid.NewGuid(), BookingId = booking, Amount = v.Fee, Status = v.Pay, AmountReceived = v.Received,
                    RefundAmount = v.Refund, CreatedAt = now, UpdatedAt = now
                });
                if (v.Rx) db.PrescriptionGroups.Add(new PrescriptionGroup { GroupId = Guid.NewGuid(), BookingId = booking, PatientId = patient, DoctorId = d.DoctorId, CreatedAt = now, UpdatedAt = now });
                await db.SaveChangesAsync();

                var needsConsult = v.Consult || v.ConsultMinutes is not null || v.Diagnoses is not null || v.Cert || v.Lab || v.FollowUp is not null;
                if (!needsConsult) return 0;
                var cid = Guid.NewGuid();
                var created = now.AddHours(-1);
                db.Consultations.Add(new Consultation
                {
                    ConsultationId = cid, BookingId = booking, PatientId = patient, DoctorId = d.DoctorId, Status = ConsultationStatus.Completed,
                    CreatedAt = created, UpdatedAt = now, CompletedAt = v.ConsultMinutes is { } m ? created.AddMinutes(m) : null
                });
                await db.SaveChangesAsync();
                foreach (var (code, text) in v.Diagnoses ?? [])
                    db.ConsultationDiagnoses.Add(new ConsultationDiagnosis { Id = Guid.NewGuid(), ConsultationId = cid, Icd10Code = code, CustomDescription = text, Type = DiagnosisType.Primary, CreatedAt = now });
                if (v.Cert) db.MedicalCertificates.Add(new MedicalCertificate { CertificateId = Guid.NewGuid(), ConsultationId = cid, PatientId = patient, DoctorId = d.DoctorId, IssueDate = v.Date, DiagnosisText = "x", CreatedAt = now, UpdatedAt = now });
                if (v.Lab) db.LabOrders.Add(new LabOrder { LabOrderId = Guid.NewGuid(), ConsultationId = cid, PatientId = patient, DoctorId = d.DoctorId, TestName = "CBC", Status = LabOrderStatus.Requested, RequestedAt = now, CreatedAt = now, UpdatedAt = now });
                if (v.FollowUp is { } fu) db.FollowUps.Add(new FollowUp { Id = Guid.NewGuid(), ConsultationId = cid, PatientId = patient, DoctorId = d.DoctorId, FollowUpDate = fu, Reason = "recheck", Status = v.FollowUpStatus, CreatedAt = now, UpdatedAt = now });
                await db.SaveChangesAsync();
                return 0;
            });
        }
    }

    private HttpClient AsDoctor(Doc d) => F.ClientFor(d.UserId, "Doctor");
    private static async Task<JsonElement> Perf(HttpClient c, string period = "today", Guid? doctorId = null)
    {
        var res = await c.GetAsync($"/api/doctor/performance?period={period}" + (doctorId is null ? "" : $"&doctorId={doctorId}"));
        await ShouldBe(HttpStatusCode.OK, res);
        return await JsonOf(res);
    }
    private static int I(JsonElement e, string block, string prop) => e.GetProperty(block).GetProperty(prop).GetInt32();
    private static decimal M(JsonElement e, string block, string prop) => e.GetProperty(block).GetProperty(prop).GetDecimal();

    // ── authz ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Anonymous_is_401_and_staff_and_patient_are_403()
    {
        await ShouldBe(HttpStatusCode.Unauthorized, await F.Anonymous().GetAsync("/api/doctor/performance"));
        await ShouldBe(HttpStatusCode.Forbidden, await F.As(W.Staff, "Staff").GetAsync("/api/doctor/performance"));
        await ShouldBe(HttpStatusCode.Forbidden, await F.Patient(W.PatientA).GetAsync("/api/doctor/performance"));
        await ShouldBe(HttpStatusCode.Forbidden, await F.Patient(W.PatientA).GetAsync($"/api/doctor/performance?doctorId={W.DoctorA.RowId}"));
    }

    [Fact]
    public async Task Doctor_sees_only_their_own_numbers_and_a_passed_doctorId_is_ignored()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var a = await NewDoctor();
        var b = await NewDoctor();
        await Seed(a, new Visit(Today), new Visit(Today, Fee: 700, Received: 700));

        var mine = await Perf(AsDoctor(a));
        Assert.Equal(2, I(mine, "current", "patients_seen"));
        Assert.Equal(a.DoctorId, mine.GetProperty("doctor_id").GetGuid());

        var bSees = await Perf(AsDoctor(b));
        Assert.Equal(0, I(bSees, "current", "patients_seen"));
        Assert.Equal(0m, M(bSees, "current", "collected"));

        // B tries to read A by passing A's id: the id is ignored, B still gets B's (empty) numbers.
        var bSpoof = await Perf(AsDoctor(b), doctorId: a.DoctorId);
        Assert.Equal(b.DoctorId, bSpoof.GetProperty("doctor_id").GetGuid());
        Assert.Equal(0, I(bSpoof, "current", "patients_seen"));
        Assert.Empty(bSpoof.GetProperty("unpaid").GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Admin_must_pass_doctorId_and_can_read_any_doctor()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var a = await NewDoctor();
        await Seed(a, new Visit(Today));
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.BadRequest, await admin.GetAsync("/api/doctor/performance?period=today"));
        await ShouldBe(HttpStatusCode.NotFound, await admin.GetAsync($"/api/doctor/performance?period=today&doctorId={Guid.NewGuid()}"));
        var seen = await Perf(admin, doctorId: a.DoctorId);
        Assert.Equal(1, I(seen, "current", "patients_seen"));
    }

    [Fact]
    public async Task Bad_period_is_400_and_default_is_today()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var a = await NewDoctor();
        using var c = AsDoctor(a);
        await ShouldBe(HttpStatusCode.BadRequest, await c.GetAsync("/api/doctor/performance?period=year"));
        var def = await JsonOf(await c.GetAsync("/api/doctor/performance"));
        Assert.Equal("today", def.GetProperty("period").GetProperty("name").GetString());
        Assert.Equal("PHP", def.GetProperty("currency").GetString());
        Assert.Equal("Monday", def.GetProperty("week_starts_on").GetString());
    }

    // ── period boundaries (Manila; week starts Monday) ────────────────────
    [Fact]
    public async Task Period_boundaries_use_manila_days_and_a_monday_week()
    {
        using var _ = FrozenClock.At(FrozenUtc); // Thu 2026-11-19 (Manila)
        var d = await NewDoctor();
        DateOnly D(int m, int day) => new(2026, m, day);
        await Seed(d,
            new Visit(D(11, 19)), new Visit(D(11, 19)),            // today x2
            new Visit(D(11, 18)),                                    // yesterday
            new Visit(D(11, 16)),                                    // Monday (week start)
            new Visit(D(11, 15)),                                    // Sunday — before this week, after previous span (Mon-Thu) → in neither
            new Visit(D(11, 12)), new Visit(D(11, 9)),               // previous week up to the same weekday
            new Visit(D(11, 8)),                                     // Sunday before previous week
            new Visit(D(11, 1)),                                     // month start
            new Visit(D(10, 31)),                                    // outside previous-month span (1..19)
            new Visit(D(10, 19)),                                    // last day of previous-month span
            new Visit(D(10, 20)));                                   // just outside it

        var today = await Perf(AsDoctor(d), "today");
        Assert.Equal("2026-11-19", today.GetProperty("period").GetProperty("from").GetString());
        Assert.Equal(2, I(today, "current", "patients_seen"));
        Assert.Equal(1, I(today, "previous", "patients_seen"));
        Assert.Equal("2026-11-18", today.GetProperty("previous_period").GetProperty("from").GetString());

        var week = await Perf(AsDoctor(d), "week");
        Assert.Equal("2026-11-16", week.GetProperty("period").GetProperty("from").GetString()); // Monday
        Assert.Equal("2026-11-19", week.GetProperty("period").GetProperty("to").GetString());
        Assert.Equal(4, I(week, "current", "patients_seen"));   // 16, 18, 19, 19
        Assert.Equal("2026-11-09", week.GetProperty("previous_period").GetProperty("from").GetString());
        Assert.Equal("2026-11-12", week.GetProperty("previous_period").GetProperty("to").GetString());
        Assert.Equal(2, I(week, "previous", "patients_seen"));  // 9, 12

        var month = await Perf(AsDoctor(d), "month");
        Assert.Equal("2026-11-01", month.GetProperty("period").GetProperty("from").GetString());
        Assert.Equal(9, I(month, "current", "patients_seen"));  // 1, 8, 9, 12, 15, 16, 18, 19, 19
        Assert.Equal("2026-10-01", month.GetProperty("previous_period").GetProperty("from").GetString());
        Assert.Equal("2026-10-19", month.GetProperty("previous_period").GetProperty("to").GetString());
        Assert.Equal(1, I(month, "previous", "patients_seen")); // 10-19 only
    }

    [Fact]
    public async Task Previous_month_span_is_clamped_when_the_month_is_shorter()
    {
        using var _ = FrozenClock.At("2026-03-30T04:00:00Z"); // 12:00 Manila, Mon 2026-03-30
        var d = await NewDoctor();
        var month = await Perf(AsDoctor(d), "month");
        Assert.Equal("2026-02-01", month.GetProperty("previous_period").GetProperty("from").GetString());
        Assert.Equal("2026-02-28", month.GetProperty("previous_period").GetProperty("to").GetString());
        var week = await Perf(AsDoctor(d), "week");
        Assert.Equal("2026-03-30", week.GetProperty("period").GetProperty("from").GetString()); // Monday == today
    }

    // ── money rules ───────────────────────────────────────────────────────
    [Fact]
    public async Task Money_refunded_is_not_collected_and_paid_but_not_completed_is_excluded()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        await Seed(d,
            new Visit(Today, Fee: 500, Received: 500),                                                    // paid in full
            new Visit(Today, Fee: 300, Received: null),                                                   // legacy paid: falls back to amount
            new Visit(Today, Fee: 450, Pay: PaymentStatus.Waived),                                        // waived
            new Visit(Today, Fee: 450, Pay: PaymentStatus.Refunded, Received: 450, Refund: 450),          // refunded: NOT collected
            new Visit(Today, Fee: 450, Due: 450, Pay: PaymentStatus.Unpaid),                              // unpaid
            new Visit(Today, Status: BookingStatus.Confirmed, Fee: 600, Pay: PaymentStatus.Paid, Received: 600)); // paid online, not seen yet: excluded

        var r = await Perf(AsDoctor(d));
        Assert.Equal(5, I(r, "current", "patients_seen"));
        Assert.Equal(800m, M(r, "current", "collected"));   // 500 + 300 only
        Assert.Equal(2150m, M(r, "current", "billed"));     // 500+300+450+450+450 (the 600 is excluded)
        Assert.Equal(450m, M(r, "current", "waived"));
        Assert.Equal(450m, M(r, "current", "refunded"));
        Assert.Equal(450m, M(r, "current", "unpaid_total"));
    }

    [Fact]
    public async Task First_time_patients_and_new_vs_followup_visits()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        var returning = await NewPatient("Ret");
        var brandNew = await NewPatient("New");
        await Seed(d,
            new Visit(new DateOnly(2026, 9, 1), Patient: returning),                              // earlier completed visit
            new Visit(Today, Type: VisitType.FollowUp, Patient: returning),
            new Visit(Today, Type: VisitType.New, Patient: brandNew),
            new Visit(Today, Type: VisitType.FollowUp, Patient: brandNew));                       // same patient twice today

        var r = await Perf(AsDoctor(d));
        Assert.Equal(3, I(r, "current", "patients_seen"));
        Assert.Equal(2, I(r, "current", "unique_patients"));
        Assert.Equal(1, I(r, "current", "new_visits"));
        Assert.Equal(2, I(r, "current", "follow_up_visits"));
        Assert.Equal(1, I(r, "current", "first_time_patients")); // only brandNew
    }

    // ── series ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Series_has_14_consecutive_zero_filled_days_ending_today()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        await Seed(d, new Visit(Today, Fee: 500, Received: 500), new Visit(Today.AddDays(-5), Fee: 450, Received: 450), new Visit(Today.AddDays(-14), Fee: 450, Received: 450)); // -14 is outside the window

        var r = await Perf(AsDoctor(d));
        var days = r.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(14, days.Count);
        Assert.Equal("2026-11-06", days[0].GetProperty("date").GetString());
        Assert.Equal("2026-11-19", days[^1].GetProperty("date").GetString());
        for (var i = 1; i < days.Count; i++)
            Assert.Equal(DateOnly.Parse(days[i - 1].GetProperty("date").GetString()!).AddDays(1), DateOnly.Parse(days[i].GetProperty("date").GetString()!));
        Assert.Equal(1, days[^1].GetProperty("patients_seen").GetInt32());
        Assert.Equal(500m, days[^1].GetProperty("collected").GetDecimal());
        Assert.Equal(1, days[8].GetProperty("patients_seen").GetInt32()); // 2026-11-14
        Assert.Equal(2, days.Count(x => x.GetProperty("patients_seen").GetInt32() > 0));
        Assert.All(days.Where(x => x.GetProperty("patients_seen").GetInt32() == 0), x => Assert.Equal(0m, x.GetProperty("collected").GetDecimal()));
    }

    [Fact]
    public async Task A_doctor_with_no_data_gets_a_full_zero_response()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        var r = await Perf(AsDoctor(d), "month");
        Assert.Equal(14, r.GetProperty("series").GetArrayLength());
        Assert.Equal(0, I(r, "current", "patients_seen"));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("current").GetProperty("avg_consult_minutes").ValueKind);
        Assert.Equal(0, r.GetProperty("top_diagnoses").GetArrayLength());
        Assert.Equal(0, r.GetProperty("unpaid").GetProperty("count").GetInt32());
        Assert.Equal(0, r.GetProperty("follow_ups_due").GetProperty("count").GetInt32());
    }

    // ── counts, avg consult, diagnoses, lists ─────────────────────────────
    [Fact]
    public async Task Counts_for_rx_certs_labs_noshows_and_cancellations()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        await Seed(d,
            new Visit(Today, Rx: true, Cert: true, Lab: true), new Visit(Today, Rx: true),
            new Visit(Today, Status: BookingStatus.NoShow, Pay: PaymentStatus.Unpaid),
            new Visit(Today, Status: BookingStatus.Cancelled, Pay: PaymentStatus.Unpaid),
            new Visit(Today, Status: BookingStatus.Expired, Pay: PaymentStatus.Unpaid),
            new Visit(Today.AddDays(-1), Rx: true, Cert: true, Lab: true)); // yesterday: not in "today"
        var r = await Perf(AsDoctor(d));
        Assert.Equal(2, I(r, "current", "patients_seen"));
        Assert.Equal(2, I(r, "current", "rx_groups"));
        Assert.Equal(1, I(r, "current", "med_certs"));
        Assert.Equal(1, I(r, "current", "lab_orders"));
        Assert.Equal(1, I(r, "current", "no_shows"));
        Assert.Equal(2, I(r, "current", "cancellations"));
        Assert.Equal(1, I(r, "previous", "rx_groups"));
        Assert.Equal(1, I(r, "previous", "med_certs"));
    }

    [Fact]
    public async Task Average_consult_minutes_is_null_below_5_samples_and_ignores_open_drafts()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        await Seed(d, new Visit(Today, ConsultMinutes: 10), new Visit(Today, ConsultMinutes: 10), new Visit(Today, ConsultMinutes: 10), new Visit(Today, ConsultMinutes: 10),
            new Visit(Today, ConsultMinutes: 600)); // a draft left open for 10h is not a visit length
        var four = await Perf(AsDoctor(d));
        Assert.Equal(4, I(four, "current", "avg_consult_samples"));
        Assert.Equal(JsonValueKind.Null, four.GetProperty("current").GetProperty("avg_consult_minutes").ValueKind);

        await Seed(d, new Visit(Today, ConsultMinutes: 20));
        var five = await Perf(AsDoctor(d));
        Assert.Equal(5, I(five, "current", "avg_consult_samples"));
        Assert.Equal(12.0, five.GetProperty("current").GetProperty("avg_consult_minutes").GetDouble(), 1); // (10*4+20)/5
    }

    [Fact]
    public async Task Top_diagnoses_are_the_top_5_with_icd_names_and_visit_dedupe()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        var tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        await WithDb(async db =>
        {
            for (var i = 1; i <= 6; i++) db.Icd10Codes.Add(new Icd10Code { Code = $"Z{tag}{i}", Description = $"Condition {i}" });
            await db.SaveChangesAsync();
            return 0;
        });
        var visits = new List<Visit>();
        for (var i = 1; i <= 6; i++)                      // Z..1 seen 6x, Z..2 5x, ... Z..6 1x
            for (var n = 0; n < 7 - i; n++) visits.Add(new Visit(Today, Diagnoses: [($"Z{tag}{i}", null)]));
        visits.Add(new Visit(Today, Diagnoses: [($"Z{tag}6", null), ($"Z{tag}6", null)]));            // same code twice in ONE visit counts once
        visits.Add(new Visit(Today, Diagnoses: [(null, "  Sore throat "), (null, "sore throat")]));  // free text, case-insensitive, once per visit
        await Seed(d, visits.ToArray());

        var r = await Perf(AsDoctor(d));
        var top = r.GetProperty("top_diagnoses").EnumerateArray().ToList();
        Assert.Equal(5, top.Count);
        Assert.Equal($"Z{tag}1", top[0].GetProperty("icd10_code").GetString());
        Assert.Equal("Condition 1", top[0].GetProperty("label").GetString());
        Assert.Equal(6, top[0].GetProperty("count").GetInt32());
        Assert.Equal(5, top[1].GetProperty("count").GetInt32());
        Assert.DoesNotContain(top, t => t.GetProperty("icd10_code").GetString() == $"Z{tag}6" && t.GetProperty("count").GetInt32() > 2);
    }

    [Fact]
    public async Task Unpaid_list_is_bounded_newest_first_with_full_totals()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        var other = await NewDoctor();
        var visits = Enumerable.Range(0, 22).Select(i => new Visit(Today.AddDays(-i), Fee: 100 + i, Due: 100 + i, Pay: PaymentStatus.Unpaid)).ToList();
        visits.Add(new Visit(Today, Fee: 450, Due: 450, Status: BookingStatus.Confirmed, Pay: PaymentStatus.Unpaid)); // not completed → not "unpaid completed"
        await Seed(d, visits.ToArray());
        await Seed(other, new Visit(Today, Fee: 999, Due: 999, Pay: PaymentStatus.Unpaid));                          // another doctor's debt never shows

        var r = await Perf(AsDoctor(d));
        var u = r.GetProperty("unpaid");
        Assert.Equal(22, u.GetProperty("count").GetInt32());
        Assert.Equal(Enumerable.Range(0, 22).Sum(i => 100m + i), u.GetProperty("total").GetDecimal());
        var items = u.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(20, items.Count);
        Assert.Equal("2026-11-19", items[0].GetProperty("appointment_date").GetString());
        Assert.True(string.CompareOrdinal(items[0].GetProperty("appointment_date").GetString(), items[19].GetProperty("appointment_date").GetString()) > 0);
        Assert.Contains("Perf", items[0].GetProperty("patient_name").GetString());
    }

    [Fact]
    public async Task Follow_ups_due_are_pending_next_7_days_and_this_doctors_only()
    {
        using var _ = FrozenClock.At(FrozenUtc);
        var d = await NewDoctor();
        var other = await NewDoctor();
        await Seed(d,
            new Visit(Today.AddDays(-3), FollowUp: Today),                                               // due today ✔
            new Visit(Today.AddDays(-3), FollowUp: Today.AddDays(7)),                                    // last day ✔
            new Visit(Today.AddDays(-3), FollowUp: Today.AddDays(8)),                                    // too far
            new Visit(Today.AddDays(-3), FollowUp: Today.AddDays(-1)),                                   // overdue (past)
            new Visit(Today.AddDays(-3), FollowUp: Today.AddDays(2), FollowUpStatus: FollowUpStatus.Completed)); // already done
        await Seed(other, new Visit(Today.AddDays(-3), FollowUp: Today.AddDays(1)));

        var r = await Perf(AsDoctor(d));
        var f = r.GetProperty("follow_ups_due");
        Assert.Equal(2, f.GetProperty("count").GetInt32());
        var items = f.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "2026-11-19", "2026-11-26" }, items.Select(i => i.GetProperty("follow_up_date").GetString()).ToArray());
        Assert.Equal("recheck", items[0].GetProperty("reason").GetString());
    }
}
//JJQR - END
