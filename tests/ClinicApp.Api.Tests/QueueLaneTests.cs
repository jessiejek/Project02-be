using System.Net;
using System.Net.Http.Json;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 21, 2026) - priority lane: R-001 regular / S-001 senior-PWD, seniors always sorted first
/// <summary>Pure lane rules (no database).</summary>
public class QueueLaneRuleTests
{
    private static readonly DateOnly Day = new(2026, 11, 21);

    private static Patient P(DateOnly dob, string? senior = null, string? pwd = null) =>
        new() { DateOfBirth = dob, SeniorIdNumber = senior, PwdIdNumber = pwd };

    [Fact] public void A_regular_adult_is_in_the_regular_lane() =>
        Assert.Equal("R", QueueLane.For(P(new DateOnly(1990, 1, 1)), null, Day));

    [Fact] public void Sixty_on_the_visit_day_is_a_senior() =>
        Assert.Equal("S", QueueLane.For(P(new DateOnly(1966, 11, 21)), null, Day));

    [Fact] public void Turning_sixty_tomorrow_is_not_yet_a_senior() =>
        Assert.Equal("R", QueueLane.For(P(new DateOnly(1966, 11, 22)), null, Day));

    [Fact] public void A_picked_senior_or_pwd_discount_puts_anyone_in_the_priority_lane()
    {
        Assert.Equal("S", QueueLane.For(P(new DateOnly(1990, 1, 1)), "PWD", Day));
        Assert.Equal("S", QueueLane.For(P(new DateOnly(1990, 1, 1)), "Senior", Day));
        Assert.Equal("R", QueueLane.For(P(new DateOnly(1990, 1, 1)), "  ", Day));
    }

    //JJQR - AI (Nov 21, 2026) - only a real Senior / PWD category counts; any other text is not a discount and does not grant the priority lane
    [Fact] public void Any_other_discount_text_is_not_a_discount_and_does_not_give_the_priority_lane()
    {
        Assert.Equal("R", QueueLane.For(P(new DateOnly(1990, 1, 1)), "x", Day));
        Assert.Equal("R", QueueLane.For(P(new DateOnly(1990, 1, 1)), "VIP", Day));
        Assert.Equal("S", QueueLane.For(P(new DateOnly(1990, 1, 1)), "pwd", Day));      // any casing of a real category
        Assert.Equal("Senior", QueueLane.NormalizeDiscount(" senior "));
        Assert.Equal("PWD", QueueLane.NormalizeDiscount("Pwd"));
        Assert.Null(QueueLane.NormalizeDiscount("x"));
        Assert.True(QueueLane.IsValidDiscount(null));
        Assert.True(QueueLane.IsValidDiscount("  "));
        Assert.False(QueueLane.IsValidDiscount("x"));
    }

    [Fact] public void A_senior_or_pwd_id_on_file_is_enough() =>
        Assert.All(new[] { P(new DateOnly(1990, 1, 1), senior: "SC-1"), P(new DateOnly(1990, 1, 1), pwd: "PWD-1") },
            p => Assert.Equal("S", QueueLane.For(p, null, Day)));

    [Fact] public void An_unset_birth_date_is_not_treated_as_a_senior() =>
        Assert.Equal("R", QueueLane.For(P(default), null, Day));

    [Fact] public void Unknown_patient_without_a_discount_is_regular() =>
        Assert.Equal("R", QueueLane.For(null, null, Day));

    [Fact] public void Numbers_are_lane_dash_three_digits_and_only_S_is_priority()
    {
        Assert.Equal("S-003", QueueLane.Format("S", 3));
        Assert.Equal("R-014", QueueLane.Format("R", 14));
        Assert.True(QueueLane.IsPriority("S-003"));
        Assert.False(QueueLane.IsPriority("R-014"));
        Assert.False(QueueLane.IsPriority("Q-052")); // pre-lane numbers are regular
        Assert.False(QueueLane.IsPriority(null));
    }
}

/// <summary>Check-in through the real API: numbering per lane, and the board order.</summary>
[Collection("api")]
public class QueueLaneFlowTests(ApiFixture api) : ApiTestBase(api)
{
    private Task<Guid> NewPatientAsync(DateOnly dob, string? seniorId = null) => WithDb(async db =>
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Patients.Add(new Patient
        {
            PatientId = id, PatientCode = $"MF-L{id:N}"[..12], FirstName = "Lane", LastName = "Tester",
            DateOfBirth = dob, Sex = SexType.Female, Email = $"lane-{id:N}@test.local", ContactNumber = "09170000000",
            SeniorIdNumber = seniorId, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return id;
    });

    private async Task<(Guid Booking, string Number, bool IsPriority)> CheckInAsync(Guid patientId, string? discount = null)
    {
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PostAsJsonAsync("/api/queue", new { patient_id = patientId, discount_category = discount });
        await ShouldBe(HttpStatusCode.OK, res);
        var json = await JsonOf(res);
        return (json.GetProperty("booking_id").GetGuid(), json.GetProperty("queue_number").GetString()!, json.GetProperty("is_priority").GetBoolean());
    }

    private static int Seq(string number) => int.Parse(number[2..]);

    [Fact]
    public async Task Regular_patients_get_R_numbers_and_seniors_get_S_numbers_counted_separately()
    {
        var adult = await NewPatientAsync(new DateOnly(1990, 1, 1));
        var senior = await NewPatientAsync(new DateOnly(1950, 5, 5));
        var seniorId = await NewPatientAsync(new DateOnly(1985, 3, 3), seniorId: "SC-123");
        var pwdDiscount = await NewPatientAsync(new DateOnly(1992, 2, 2));

        var r1 = await CheckInAsync(adult);
        var s1 = await CheckInAsync(senior);
        var r2 = await CheckInAsync(await NewPatientAsync(new DateOnly(1991, 1, 1)));
        var s2 = await CheckInAsync(seniorId);
        var s3 = await CheckInAsync(pwdDiscount, "PWD");

        Assert.StartsWith("R-", r1.Number); Assert.False(r1.IsPriority);
        Assert.StartsWith("S-", s1.Number); Assert.True(s1.IsPriority);
        Assert.Equal(Seq(r1.Number) + 1, Seq(r2.Number));   // R keeps counting on its own
        Assert.Equal(Seq(s1.Number) + 1, Seq(s2.Number));   // S counts on its own
        Assert.Equal(Seq(s2.Number) + 1, Seq(s3.Number));
        Assert.StartsWith("S-", s3.Number);
    }

    [Fact]
    public async Task The_queue_board_lists_seniors_first_even_when_they_arrived_later()
    {
        var regular = await CheckInAsync(await NewPatientAsync(new DateOnly(1990, 1, 1)));
        var regular2 = await CheckInAsync(await NewPatientAsync(new DateOnly(1991, 1, 1)));
        var senior = await CheckInAsync(await NewPatientAsync(new DateOnly(1948, 1, 1))); // arrives LAST

        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.GetAsync("/api/queue");
        await ShouldBe(HttpStatusCode.OK, res);
        var items = (await JsonOf(res)).GetProperty("items").EnumerateArray().ToList();
        var order = items.Select(i => i.GetProperty("booking_id").GetGuid()).ToList();

        Assert.True(order.IndexOf(senior.Booking) < order.IndexOf(regular.Booking), "senior must be listed before an earlier regular");
        Assert.True(order.IndexOf(regular.Booking) < order.IndexOf(regular2.Booking), "within the regular lane it stays first-come-first-served");
        var seniorRow = items.Single(i => i.GetProperty("booking_id").GetGuid() == senior.Booking);
        Assert.True(seniorRow.GetProperty("is_priority").GetBoolean());
    }

    [Fact]
    public async Task Check_in_with_a_made_up_discount_is_refused_and_creates_nothing()
    {
        var p = await NewPatientAsync(new DateOnly(1990, 1, 1));
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PostAsJsonAsync("/api/queue", new { patient_id = p, discount_category = "x" }));
        var any = await WithDb(db => db.Bookings.AnyAsync(b => b.PatientId == p));
        Assert.False(any);
    }

    [Fact]
    public async Task An_online_booking_by_a_senior_gets_an_S_number()
    {
        var seniorPatientNumber = await WithDb(async db =>
        {
            var id = await NewPatientAsync(new DateOnly(1945, 6, 6));
            return await QueueSequencer.NextForPatientAsync(db, ClinicClock.Today, id, null, CancellationToken.None);
        });
        Assert.StartsWith("S-", seniorPatientNumber);
    }
}
//JJQR - END
