using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
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
    });

builder.Services.AddAuthorization();

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
        RootPath = Path.Combine(env.ContentRootPath, "App_Data", "uploads"),
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
}

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

// §17.2 — correlation id + structured request logging, before anything that logs.
app.UseMiddleware<RequestContextMiddleware>();

app.UseHttpsRedirection();

// Serve uploaded files (patient documents / lab results) from App_Data/uploads.
var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads");
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
