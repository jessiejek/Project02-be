using System.Net;
using System.Text.RegularExpressions;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicApp.Api.Tests;

/// <summary>Wraps the real allocator but first hands out codes that are already taken, to force the
/// unique-index collision path deterministically.</summary>
public sealed class CollidingAllocator(IPatientCodeAllocator inner, string takenCode, int collisions) : IPatientCodeAllocator
{
    private int _left = collisions;
    public int Calls;

    public Task<string> NextAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Calls);
        return Interlocked.Decrement(ref _left) >= 0 ? Task.FromResult(takenCode) : inner.NextAsync(db, ct);
    }
}

/// <summary>P0.3 — `patient_code` is issued by the server only: monotonic, unique, collision-safe.</summary>
[Collection("api")]
public partial class PatientCodeTests(ApiFixture api) : ApiTestBase(api)
{
    [GeneratedRegex(@"^MF-\d{6}$")] private static partial Regex CodeFormat();

    private static object NewPatient(string first) => new
    {
        first_name = first, last_name = "Codetest", date_of_birth = "1991-02-03", sex = "Female",
        email = "", is_guest = true
    };

    private async Task<string> CreateAs(HttpClient client, object body)
    {
        var res = await client.PostAsync("/api/patients", ApiFactory.Body(body));
        await ShouldBe(HttpStatusCode.Created, res);
        return (await JsonOf(res)).GetProperty("patient_code").GetString()!;
    }

    // ── format / ownership of the code ───────────────────────────────────
    [Fact]
    public async Task Server_issues_the_code_in_MF_000000_format()
    {
        using var staff = F.As(W.Staff, "Staff");
        var code = await CreateAs(staff, NewPatient("Server"));
        Assert.Matches(CodeFormat(), code);
    }

    [Theory]
    [InlineData("MF-1234")]            // the old client-side random format
    [InlineData("MF-100000")]          // a well-formed but client-chosen code
    [InlineData("ANYTHING")]
    public async Task A_client_supplied_code_is_ignored(string clientCode)
    {
        using var staff = F.As(W.Staff, "Staff");
        var code = await CreateAs(staff, new
        {
            first_name = "Client", last_name = "Supplied", date_of_birth = "1991-02-03", sex = "Male", email = "",
            patient_code = clientCode
        });
        Assert.NotEqual(clientCode, code);
        Assert.Matches(CodeFormat(), code);
        Assert.False(await WithDb(db => db.Patients.AnyAsync(p => p.PatientCode == clientCode && p.FirstName == "Client")));
    }

    [Fact]
    public async Task Codes_are_strictly_increasing_and_unique()
    {
        using var admin = F.As(W.Admin, "Admin");
        var codes = new List<long>();
        for (var i = 0; i < 8; i++)
            codes.Add(long.Parse((await CreateAs(admin, NewPatient("Seq" + i)))[3..]));
        Assert.Equal(codes.OrderBy(c => c), codes);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public async Task Legacy_codes_in_other_formats_dont_confuse_the_allocator()
    {
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            // the old client-random format, the old register format, and a hand-typed code
            foreach (var legacy in new[] { "MF-1234", "P-20260101-ABC123", "MF-12ab-manual", "MF-99999999x" })
                db.Patients.Add(new Patient { PatientId = Guid.NewGuid(), PatientCode = legacy, FirstName = "Old", LastName = "Code", DateOfBirth = new DateOnly(1970, 1, 1), Sex = SexType.Male, Email = "", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");
        var code = await CreateAs(staff, NewPatient("AfterLegacy"));
        Assert.Matches(CodeFormat(), code);
        Assert.True(long.Parse(code[3..]) >= 100000); // not "MF-001235" derived from the legacy MF-1234
    }

    [Fact]
    public async Task The_allocator_continues_past_the_highest_existing_code()
    {
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Patients.Add(new Patient { PatientId = Guid.NewGuid(), PatientCode = "MF-900000", FirstName = "High", LastName = "Water", DateOfBirth = new DateOnly(1970, 1, 1), Sex = SexType.Male, Email = "", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        });
        using var staff = F.As(W.Staff, "Staff");
        Assert.True(long.Parse((await CreateAs(staff, NewPatient("PastMax")))[3..]) > 900000);
    }

    [Fact]
    public async Task Patients_and_doctors_roles_cannot_be_used_to_skip_authz_on_create()
    {
        using var p = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.Forbidden, await p.PostAsync("/api/patients", ApiFactory.Body(NewPatient("Nope"))));
    }

    // ── concurrency & collisions ─────────────────────────────────────────
    [Fact]
    public async Task Concurrent_creates_all_succeed_with_unique_codes_and_no_lost_rows()
    {
        const int n = 20;
        var before = await WithDb(db => db.Patients.CountAsync(p => p.LastName == "Parallel"));
        using var staff = F.As(W.Staff, "Staff");
        var results = await Task.WhenAll(Enumerable.Range(0, n).Select(async i =>
        {
            var res = await staff.PostAsync("/api/patients", ApiFactory.Body(new { first_name = "P" + i, last_name = "Parallel", date_of_birth = "1991-02-03", sex = "Male", email = "" }));
            return (res.StatusCode, Code: res.IsSuccessStatusCode ? (await JsonOf(res)).GetProperty("patient_code").GetString() : null);
        }));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(n, results.Select(r => r.Code).Distinct().Count());
        Assert.All(results, r => Assert.Matches(CodeFormat(), r.Code!));
        Assert.Equal(before + n, await WithDb(db => db.Patients.CountAsync(p => p.LastName == "Parallel")));
        // …and the database agrees: no duplicate code anywhere.
        Assert.Equal(0, await WithDb(db => db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM (SELECT patient_code FROM patients GROUP BY patient_code HAVING COUNT(*) > 1)").SingleAsync()));
    }

    [Fact]
    public async Task The_database_itself_rejects_a_duplicate_code()
    {
        var code = await WithDb(db => db.Patients.Where(p => p.PatientId == W.PatientA.RowId).Select(p => p.PatientCode).SingleAsync());
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Patients.Add(new Patient { PatientId = Guid.NewGuid(), PatientCode = code, FirstName = "Dup", LastName = "Dup", DateOfBirth = new DateOnly(1970, 1, 1), Sex = SexType.Male, Email = "", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        }));
        Assert.True(PatientCodes.IsCodeCollision(ex));
    }

    [Fact]
    public async Task A_collision_is_retried_and_leaves_exactly_one_clean_patient_and_audit_row()
    {
        var taken = await WithDb(db => db.Patients.Where(p => p.PatientId == W.PatientA.RowId).Select(p => p.PatientCode).SingleAsync());
        var allocator = new CollidingAllocator(new PatientCodeAllocator(), taken, collisions: 2); // two bad codes, then a real one
        using var factory = F.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IPatientCodeAllocator>();
            s.AddSingleton<IPatientCodeAllocator>(allocator);
        }));
        var token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(W.Staff.UserId, "Staff");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var res = await client.PostAsync("/api/patients", ApiFactory.Body(new { first_name = "Retry", last_name = "Collision", date_of_birth = "1991-02-03", sex = "Male", email = "" }));
        await ShouldBe(HttpStatusCode.Created, res);
        var json = await JsonOf(res);
        var code = json.GetProperty("patient_code").GetString()!;
        var id = json.GetProperty("patient_id").GetGuid();

        Assert.NotEqual(taken, code);
        Assert.Matches(CodeFormat(), code);
        Assert.Equal(3, allocator.Calls);
        Assert.Equal(1, await WithDb(db => db.Patients.CountAsync(p => p.LastName == "Collision")));
        Assert.Equal(1, await WithDb(db => db.AuditLogs.CountAsync(a => a.EntityId == id && a.Action == "Created")));
        // the original owner of the contested code is untouched
        Assert.Equal("Alice", await WithDb(db => db.Patients.Where(p => p.PatientCode == taken).Select(p => p.FirstName).SingleAsync()));
    }

    [Fact]
    public async Task If_every_attempt_collides_the_request_fails_cleanly_without_a_partial_row()
    {
        var taken = await WithDb(db => db.Patients.Where(p => p.PatientId == W.PatientA.RowId).Select(p => p.PatientCode).SingleAsync());
        var allocator = new CollidingAllocator(new PatientCodeAllocator(), taken, collisions: int.MaxValue);
        using var factory = F.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IPatientCodeAllocator>();
            s.AddSingleton<IPatientCodeAllocator>(allocator);
        }));
        var token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(W.Staff.UserId, "Staff");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        // Gave up after 5 attempts and surfaced the failure (an unhandled exception in the test
        // server; a 500 in production) — it is not swallowed, and nothing was half-saved.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() =>
            client.PostAsync("/api/patients", ApiFactory.Body(new { first_name = "Never", last_name = "Saved", date_of_birth = "1991-02-03", sex = "Male", email = "" })));
        Assert.True(PatientCodes.IsCodeCollision(ex));
        Assert.Equal(5, allocator.Calls);
        Assert.False(await WithDb(db => db.Patients.AnyAsync(p => p.LastName == "Saved")));
    }

    // ── self-registration uses the same allocator ────────────────────────
    // The /api/auth/* layer is camelCase on the wire (unlike the snake_case data resources).
    private static StringContent Registration(string email) => new(
        System.Text.Json.JsonSerializer.Serialize(new
        {
            firstName = "Reg", lastName = "Istered", email, password = "Sup3r-secret-pw!",
            dateOfBirth = "1995-05-05", sex = "Female", contactNumber = "09170000000"
        }), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Self_registration_gets_a_server_MF_code_not_the_old_random_P_code()
    {
        var email = $"reg-{Guid.NewGuid():N}@test.local";
        using var anon = F.Anonymous();
        var res = await anon.PostAsync("/api/auth/register", Registration(email));
        await ShouldBe(HttpStatusCode.OK, res);
        var code = await WithDb(db => db.Patients.Where(p => p.Email == email).Select(p => p.PatientCode).SingleAsync());
        Assert.Matches(CodeFormat(), code);
    }

    [Fact]
    public async Task Registration_survives_a_collision_without_orphaning_the_user()
    {
        var taken = await WithDb(db => db.Patients.Where(p => p.PatientId == W.PatientA.RowId).Select(p => p.PatientCode).SingleAsync());
        var allocator = new CollidingAllocator(new PatientCodeAllocator(), taken, collisions: 1);
        using var factory = F.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IPatientCodeAllocator>();
            s.AddSingleton<IPatientCodeAllocator>(allocator);
        }));
        var email = $"collide-{Guid.NewGuid():N}@test.local";
        using var client = factory.CreateClient();
        await ShouldBe(HttpStatusCode.OK, await client.PostAsync("/api/auth/register", Registration(email)));

        Assert.Equal(1, await WithDb(db => db.Users.CountAsync(u => u.Email == email)));
        Assert.Equal(1, await WithDb(db => db.Patients.CountAsync(p => p.Email == email)));
        var userId = await WithDb(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        Assert.Equal(1, await WithDb(db => db.Profiles.CountAsync(p => p.Id == userId)));
        Assert.Matches(CodeFormat(), await WithDb(db => db.Patients.Where(p => p.Email == email).Select(p => p.PatientCode).SingleAsync()));
    }

    // ── pure detection ───────────────────────────────────────────────────
    [Theory]
    [InlineData("Cannot insert duplicate key row in object 'dbo.patients' with unique index 'ix_patients_patient_code'. The duplicate key value is (MF-100001).", true)]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: patients.patient_code'.", true)]
    [InlineData("Cannot insert duplicate key row in object 'dbo.users' with unique index 'ix_users_email'.", false)]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: users.email'.", false)]
    [InlineData("Timeout expired.", false)]
    public void Only_a_patient_code_unique_violation_counts_as_a_code_collision(string innerMessage, bool expected)
    {
        var ex = new DbUpdateException("An error occurred while saving the entity changes.", new InvalidOperationException(innerMessage));
        Assert.Equal(expected, PatientCodes.IsCodeCollision(ex));
    }
}
