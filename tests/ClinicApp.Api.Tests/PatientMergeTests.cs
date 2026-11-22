//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.4 (D4): an admin merges two patients by hand
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class PatientMergeTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<Guid> NewPatientAsync(string first, string? address = null, string contact = "0917", Guid? userId = null) => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Patients.Add(new Patient
        {
            PatientId = id, PatientCode = "MG-" + id.ToString("N")[..10], FirstName = first, LastName = "Merge" + id.ToString("N")[..4], DateOfBirth = new DateOnly(1988, 8, 8), Sex = SexType.Female,
            Email = $"{id:N}@test.local", ContactNumber = contact, Address = address, UserId = userId, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    });

    private object Body(Guid keep, Guid merge, string note = "Same person registered twice") => new { keep_patient_id = keep, merge_patient_id = merge, note };

    [Fact]
    public async Task Merging_moves_everything_fills_blanks_keeps_the_kept_values_and_marks_the_other_merged()
    {
        var keep = await NewPatientAsync("Keeper", address: null, contact: "0917-KEEP");
        var merge = await NewPatientAsync("Mergee", address: "12 Old Street", contact: "0999-OTHER");
        var (b1, _) = await WithDb(db => World.NewBookingAsync(db, merge, W.DoctorA.RowId));
        var (b2, _) = await WithDb(db => World.NewBookingAsync(db, merge, W.DoctorA.RowId));
        var (bk, _) = await WithDb(db => World.NewBookingAsync(db, keep, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.PatientAllergies.AddRange(
                new PatientAllergy { Id = Guid.NewGuid(), PatientId = keep, Allergen = "Penicillin", CreatedAt = now, UpdatedAt = now },
                new PatientAllergy { Id = Guid.NewGuid(), PatientId = merge, Allergen = " penicillin ", CreatedAt = now, UpdatedAt = now },   // same allergen: not copied twice
                new PatientAllergy { Id = Guid.NewGuid(), PatientId = merge, Allergen = "Latex", CreatedAt = now, UpdatedAt = now });
            db.SyncReviewItems.Add(new SyncReviewItem
            {
                Id = Guid.NewGuid(), Kind = "possible_duplicate_patient", Status = "open", Title = "Possible duplicate patient", PatientId = merge, OtherPatientId = keep,
                Aggregate = "patient", EntityId = merge.ToString(), OtherEntityId = keep.ToString(), CreatedAt = now,
            });
            await db.SaveChangesAsync();
            return 0;
        });
        var rvBefore = await WithDb(db => db.Bookings.AsNoTracking().Where(b => b.BookingId == b1).Select(b => b.RowVersion).SingleAsync());

        using var admin = F.As(W.Admin, "Admin");
        var res = await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, merge)));
        await ShouldBe(HttpStatusCode.OK, res);
        var json = await JsonOf(res);
        Assert.Equal(2, json.GetProperty("moved").GetProperty("bookings").GetInt32());

        await WithDb(async db =>
        {
            Assert.Equal(new[] { b1, b2, bk }.OrderBy(x => x), await db.Bookings.Where(b => b.PatientId == keep).Select(b => b.BookingId).OrderBy(x => x).ToListAsync());
            Assert.Equal(0, await db.Bookings.CountAsync(b => b.PatientId == merge));
            var k = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == keep);
            Assert.Equal("12 Old Street", k.Address);              // the blank was filled
            Assert.Equal("0917-KEEP", k.ContactNumber);            // a value the kept record already had is NOT overwritten
            var m = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == merge);
            Assert.NotNull(m.DeletedAt);
            Assert.Equal(keep, m.MergedIntoPatientId);
            var allergens = (await db.PatientAllergies.Where(a => a.PatientId == keep).Select(a => a.Allergen.Trim().ToLower()).ToListAsync());
            Assert.Equal(new[] { "latex", "penicillin" }, allergens.OrderBy(x => x).ToArray());     // the union, once each
            var item = await db.SyncReviewItems.SingleAsync(i => i.PatientId == merge && i.OtherPatientId == keep);
            Assert.Equal("resolved", item.Status); Assert.Equal("merged", item.Resolution); Assert.Equal("Same person registered twice", item.ResolutionNote);
            var audit = await db.AuditLogs.SingleAsync(a => a.EntityId == keep && a.Action == "Patients merged");
            Assert.Contains(merge.ToString(), audit.Details); Assert.DoesNotContain("Mergee", audit.Details);   // ids, codes and counts only
            // desktops will receive it: the moved booking has a newer row version and the merged patient is in the change log as deleted
            Assert.True(await db.Bookings.AsNoTracking().Where(b => b.BookingId == b1).Select(b => b.RowVersion).SingleAsync() > rvBefore);
            Assert.True(await db.SyncChanges.AnyAsync(c => c.Aggregate == "patient" && c.EntityId == merge.ToString() && c.Deleted));
            return 0;
        });
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Doctor")]
    [InlineData("Patient")]
    public async Task Only_an_admin_can_preview_or_merge(string role)
    {
        var keep = await NewPatientAsync("A"); var merge = await NewPatientAsync("B");
        using var c = role switch { "Staff" => F.As(W.Staff, "Staff"), "Doctor" => F.As(W.DoctorA, "Doctor"), _ => F.Patient(W.PatientA) };
        await ShouldBe(HttpStatusCode.Forbidden, await c.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, merge))));
        await ShouldBe(HttpStatusCode.Forbidden, await c.GetAsync($"/api/patients/merge-preview?keep={keep}&merge={merge}"));
        Assert.Null(await WithDb(db => db.Patients.AsNoTracking().Where(p => p.PatientId == merge).Select(p => p.DeletedAt).SingleAsync()));   // nothing changed
    }

    [Fact]
    public async Task Bad_requests_are_refused_and_change_nothing()
    {
        var keep = await NewPatientAsync("A"); var merge = await NewPatientAsync("B");
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, keep))));            // the same patient
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, merge, "no"))));      // note too short
        await ShouldBe(HttpStatusCode.NotFound, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, Guid.NewGuid()))));    // unknown patient

        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, merge))));
        var third = await NewPatientAsync("C");
        await ShouldBe(HttpStatusCode.Conflict, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(third, merge))));            // already merged away
        await ShouldBe(HttpStatusCode.Conflict, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(merge, third))));
    }

    [Fact]
    public async Task Two_patients_that_both_have_portal_accounts_are_not_merged()
    {
        var (u1, u2) = await WithDb(async db =>
        {
            var ids = await db.Users.AsNoTracking().Select(u => u.Id).Take(40).ToListAsync();
            var used = (await db.Patients.AsNoTracking().Where(p => p.UserId != null).Select(p => p.UserId!.Value).ToListAsync()).ToHashSet();
            var free = ids.Where(i => !used.Contains(i)).Take(2).ToList();
            return (free[0], free[1]);
        });
        var keep = await NewPatientAsync("A", userId: u1); var merge = await NewPatientAsync("B", userId: u2);
        using var admin = F.As(W.Admin, "Admin");
        var preview = await admin.GetAsync($"/api/patients/merge-preview?keep={keep}&merge={merge}");
        await ShouldBe(HttpStatusCode.OK, preview);
        Assert.True((await JsonOf(preview)).GetProperty("both_have_accounts").GetBoolean());
        await ShouldBe(HttpStatusCode.Conflict, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, merge))));
        Assert.Null(await WithDb(db => db.Patients.AsNoTracking().Where(p => p.PatientId == merge).Select(p => p.DeletedAt).SingleAsync()));
    }

    [Fact]
    public async Task A_portal_account_that_only_the_other_patient_has_follows_the_person_and_the_preview_shows_counts()
    {
        var user = await WithDb(async db =>
        {
            var used = (await db.Patients.AsNoTracking().Where(p => p.UserId != null).Select(p => p.UserId!.Value).ToListAsync()).ToHashSet();
            return (await db.Users.AsNoTracking().Select(u => u.Id).Take(60).ToListAsync()).First(i => !used.Contains(i));
        });
        var keep = await NewPatientAsync("A"); var merge = await NewPatientAsync("B", userId: user);
        await WithDb(db => World.NewBookingAsync(db, merge, W.DoctorA.RowId));
        using var admin = F.As(W.Admin, "Admin");
        var preview = await JsonOf(await admin.GetAsync($"/api/patients/merge-preview?keep={keep}&merge={merge}"));
        Assert.Equal(1, preview.GetProperty("merge").GetProperty("counts").GetProperty("bookings").GetInt32());
        Assert.Equal(0, preview.GetProperty("keep").GetProperty("counts").GetProperty("bookings").GetInt32());

        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync("/api/patients/merge", ApiFactory.Body(Body(keep, merge))));
        await WithDb(async db =>
        {
            Assert.Equal(user, (await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == keep)).UserId);
            Assert.Null((await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == merge)).UserId);
            return 0;
        });
    }
}
//JJQR - END
