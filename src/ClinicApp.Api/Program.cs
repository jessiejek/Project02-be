using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ClinicApp.Api.Hubs;
using ClinicApp.Api.Middleware;
using ClinicApp.Auth;
using ClinicApp.Infrastructure;
using ClinicApp.Infrastructure.Files;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── JSON: snake_case wire format + string enums (contract §0, §3) ─────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        // namingPolicy: null => serialize using the exact enum member name (e.g. "PayAtClinic"),
        // matching contract §3 character-for-character. Do NOT pass SnakeCaseLower here.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        // Safety net: entity navigations are annotated with [JsonPropertyName]/[JsonIgnore] to match
        // contract §6 embed shapes without cycles, but ignore any that slip through rather than 500.
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ── EF Core, snake_case column mapping (plan §5) ───────────────────────────
// Provider is SqlServer by default (the real target per the plan). Development uses SQLite
// instead (see appsettings.Development.json "Database:Provider") purely so a demo/local run
// doesn't need a reachable SQL Server instance — schema is (re)created via EnsureCreated() below,
// NOT the checked-in SQL-Server-flavored migration (its column type strings are SQL Server
// specific and would be wrong for SQLite). The SQL Server migration remains the deployment path.
var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var isSqlite = string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<ClinicAppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (isSqlite)
    {
        options.UseSqlite(connectionString).UseSnakeCaseNamingConvention();
    }
    else
    {
        options.UseSqlServer(connectionString).UseSnakeCaseNamingConvention();
    }
    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
    }
});

// ── Auth: JWT options, token service, password hasher ──────────────────────
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<PasswordHasherService>();

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
// JJQR - AI (Nov 20, 2026) - PV-8: production must bring its own signing secret (env var Jwt__Secret); the one committed in appsettings*.json is for local development only
if (builder.Environment.IsProduction() &&
    (jwtSecret.Length < 32 || System.Text.RegularExpressions.Regex.IsMatch(jwtSecret, "change|dev|placeholder|replace|example|secret", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
{
    throw new InvalidOperationException("Jwt:Secret looks like the development placeholder. Set a long random value in the Jwt__Secret environment variable.");
}
// JJQR - END

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        // SignalR's browser transport (WebSockets/SSE) can't attach a custom
        // Authorization header, so the JS client puts the same bearer token
        // on the query string instead (accessTokenFactory) — only honor that
        // for the hub path, never for regular API requests.
        options.Events = new JwtBearerEvents
        {
            // JJQR - AI (Nov 20, 2026) - B-32: a deactivated / deleted account's still-unexpired access
            // token must stop working immediately, not at expiry
            OnTokenValidated = async context =>
            {
                //JJQR - AI (Nov 21, 2026) cloud sync: node tokens are checked against sync_nodes (active, /api/sync only)
                if (context.Principal?.IsInRole(ClinicApp.Api.Sync.SyncNodeAuth.Role) == true)
                {
                    var nodeFailure = await ClinicApp.Api.Sync.SyncNodeAuth.ValidateAsync(context.Principal, context.HttpContext);
                    if (nodeFailure is not null) context.Fail(nodeFailure);
                    return;
                }
                //JJQR - END
                var sub = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(sub, out var uid))
                {
                    context.Fail("Invalid token subject.");
                    return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<ClinicAppDbContext>();
                if (!await ClinicApp.Api.Security.AccountGuard.IsActiveAsync(db, uid, context.HttpContext.RequestAborted))
                {
                    context.Fail("Account is not active.");
                }
            },
            // JJQR - END
            //JJQR - AI (Nov 21, 2026) cloud sync: a revoked node gets 403 {"error":"node_revoked"}, not a bare 401
            OnChallenge = async context =>
            {
                if (context.HttpContext.Items.ContainsKey("sync_node_revoked"))
                {
                    context.HandleResponse();
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { error = "node_revoked" });
                }
            },
            //JJQR - END
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<ClinicApp.Api.Sync.SyncNodeAuth>(); //JJQR - AI (Nov 21, 2026) cloud sync node tokens //JJQR - END
builder.Services.AddScoped<ClinicApp.Api.Security.ActorResolver>();
builder.Services.AddScoped<BuiltInTemplateCopier>(); //JJQR - AI (Nov 20, 2026) A2
builder.Services.AddSingleton<IPatientCodeAllocator, PatientCodeAllocator>();
builder.Services.AddSignalR();

// ── Rate limiting (§17.2) — per-client-IP. A generous global fixed window plus
// a strict "auth" policy for the credential endpoints (brute-force guard).
var rl = builder.Configuration.GetSection("RateLimiting");
var globalPerMinute = rl.GetValue<int?>("GlobalPermitPerMinute") ?? 300;
var authPerMinute = rl.GetValue<int?>("AuthPermitPerMinute") ?? 10;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string ClientKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = globalPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    options.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// ── File storage (plan §7) ─────────────────────────────────────────────────
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new FileStorageOptions
    {
        RootPath = config["FileStorage:RootPath"] ?? Path.Combine(env.ContentRootPath, "App_Data", "uploads"),
        PublicPathPrefix = config["FileStorage:PublicPathPrefix"] ?? "/uploads",
        MaxFileSizeBytes = config.GetValue<long?>("FileStorage:MaxFileSizeBytes") ?? 10 * 1024 * 1024
    };
});
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

// ── CORS (plan §2 — MUST match the Next.js dev server origin, port 3000) ──
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (isSqlite)
{
    // Demo/local-only path: build the schema straight from the current EF model (no migration
    // file involved) and hand-create the 4 reporting views with SQLite syntax, since EF's ToView()
    // never generates view DDL for any provider (same reason the SQL Server migration needed
    // manual migrationBuilder.Sql calls for these).
    using var scope = app.Services.CreateScope();
    var dbDir = Path.GetDirectoryName(Path.Combine(app.Environment.ContentRootPath,
        builder.Configuration.GetConnectionString("DefaultConnection")!["Data Source=".Length..]));
    if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);

    var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
    db.Database.EnsureCreated();
    ClinicApp.Infrastructure.SqliteDemoViews.CreateViews(db);
    //JJQR - AI (Nov 21, 2026) cloud sync: add sync tables / columns to an existing SQLite file
    ClinicApp.Infrastructure.Sync.SyncBootstrap.EnsureSqliteSyncSchema(db);
    //JJQR - END
}

//JJQR - AI (Nov 20, 2026) B1 — WHO ICD-10 catalog in every environment (after EnsureCreated / migrations, before DevDataSeeder)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Icd10Seeder");
    await ClinicApp.Infrastructure.Icd10Seeder.SeedAsync(db, logger);
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - the everyday medicine catalog in every environment (formulary-checked names; idempotent)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MedicineCatalogSeeder");
    try { await ClinicApp.Infrastructure.MedicineCatalogSeeder.SeedAsync(db, logger); }
    catch (Exception ex) { logger.LogWarning(ex, "MedicineCatalogSeeder could not run; the medicine list is unchanged."); }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) - self-heal: any still-unpaid payment follows the doctor's finalised fee decision (waived stays waived, the doctor's charge amount is what is collected).
// Idempotent and logged; turn off with Billing:ReconcileOnStartup=false. A failure here never stops the API from starting.
if (builder.Configuration.GetValue("Billing:ReconcileOnStartup", true))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("FeeDecisionReconciler");
        var healed = await ClinicApp.Api.Billing.FeeDecisionReconciler.ReconcileAsync(db, null, DateTimeOffset.UtcNow, CancellationToken.None);
        if (healed.Changed)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("FeeDecisionReconciler: waived {Waived} payment(s) the doctor had waived, aligned {Aligned} amount(s) with the doctor's decision.", healed.Waived, healed.AmountsAligned);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "FeeDecisionReconciler could not run at startup; payments are unchanged.");
    }
}
//JJQR - END

// ── Dev login accounts (INTEGRATION_ROADMAP.md Phase 1 — auth cutover) ─────
// Development only. Seeds the account-list.txt users so the frontend can log in
// against this API without a full data import. No-op if the users already exist.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasherService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevDataSeeder");
    await ClinicApp.Infrastructure.DevDataSeeder.SeedAsync(db, hasher, logger);
}

//JJQR - AI (Nov 21, 2026) cloud sync: seed the change log once so a node's first pull gets everything
try
{
    using var scope = app.Services.CreateScope();
    await ClinicApp.Infrastructure.Sync.SyncBootstrap.BackfillAsync(scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>());
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Sync change-log backfill skipped (run deploy/migrate-cloud-sync.sql on SQL Server first).");
}
//JJQR - END

// §17.2 — correlation id + structured request logging, before anything that logs.
app.UseMiddleware<RequestContextMiddleware>();

// JJQR - AI (Nov 20, 2026) - PV-6: outside development, tell browsers to use HTTPS only (HSTS) as well as redirecting plain HTTP
if (!app.Environment.IsDevelopment()) app.UseHsts();
// JJQR - END
app.UseHttpsRedirection();

// Uploaded patient documents / lab results are PHI: they are NOT mounted as static
// files. They are streamed only by the authorized `GET /api/patient-documents/{id}/file`
// and `GET /api/patient-lab-results/{id}/file` endpoints (own-patient or staff-like).
Directory.CreateDirectory(app.Services.GetRequiredService<FileStorageOptions>().RootPath);

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ClinicHub>("/hubs/clinic");

// Keep-warm / uptime ping for shared hosting where the app pool idles out.
// No DB, no auth, no rate limit — the cheapest possible "is the process up"
// check. /api/settings remains the "is the DB reachable" smoke check.
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }))
    .DisableRateLimiting();

app.Run();

/// <summary>Exposes the entry point to the integration test project (WebApplicationFactory).</summary>
public partial class Program;
