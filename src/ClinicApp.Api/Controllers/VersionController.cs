using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>
/// Anonymous, no-DB-required-to-respond version/health check — answers
/// "is the deploy I just pushed actually live, and is it talking to the
/// database" without SSH-ing into the host or guessing from symptoms.
///
/// This host is a manual FTP publish (deploy/publish.sh), not CI/CD, so
/// there's no platform-injected commit SHA the way Vercel gives the
/// frontend one — publish.sh writes version.txt into the publish output at
/// build time instead, and this just reads it back.
/// </summary>
[ApiController]
[Route("api/version")]
public class VersionController(ClinicAppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Get(CancellationToken ct)
    {
        var versionFile = Path.Combine(env.ContentRootPath, "version.txt");
        var commit = System.IO.File.Exists(versionFile)
            ? (await System.IO.File.ReadAllTextAsync(versionFile, ct)).Trim()
            : "unknown";

        bool dbConnected;
        string? dbMigration = null;
        try
        {
            dbConnected = await db.Database.CanConnectAsync(ct);
            if (dbConnected)
            {
                // Migration ids are timestamp-prefixed (matches every migration
                // in this project) so the lexicographic max is also the most
                // recently applied one — no extra ordering column needed.
                dbMigration = (await db.Database.GetAppliedMigrationsAsync(ct))
                    .OrderByDescending(m => m)
                    .FirstOrDefault();
            }
        }
        catch
        {
            dbConnected = false;
        }

        return Ok(new
        {
            commit,
            environment = env.EnvironmentName,
            db_connected = dbConnected,
            db_migration = dbMigration
        });
    }
}
