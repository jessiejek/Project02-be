//JJQR - AI (Nov 24, 2026) - owner wishlist: the Admin chooses how the whole clinic prints (PDF / native / off) and uploads a background picture for each printed document.
// Every signed-in user can READ the settings (the computers keep their own copy so printing still works offline); only an Admin can change them.
using System.Text.Json.Serialization;
using ClinicApp.Api.Auditing;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Doctor,Staff")] // reading is for the clinic team only (patients never need it)
[Route("api/print-config")]
public class PrintConfigController(ClinicAppDbContext db) : ControllerBase
{
    public static readonly string[] Kinds = ["prescription", "lab_request", "medical_certificate", "queue_slip", "receipt"];
    static readonly string[] Modes = ["computer", "pdf", "native", "off"];
    const int MaxBytes = 6 * 1024 * 1024;

    public sealed record BackgroundInfo(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("size")] int Size);

    public sealed record PrintConfigDto(
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("backgrounds")] List<BackgroundInfo> Backgrounds,
        [property: JsonPropertyName("disabled")] List<string> Disabled);

    public sealed record SetEnabledRequest([property: JsonPropertyName("enabled")] bool Enabled);

    public sealed record SetModeRequest([property: JsonPropertyName("mode")] string Mode);

    // the real picture type is read from the bytes, so only the data is sent
    public sealed record SetBackgroundRequest([property: JsonPropertyName("dataBase64")] string? DataBase64);

    [HttpGet]
    public async Task<ActionResult<PrintConfigDto>> Get(CancellationToken ct)
    {
        var rows = await db.PrintConfigs.AsNoTracking().Select(c => new { c.Key, c.TextValue, c.ContentType, c.Version, Size = c.Data == null ? 0 : c.Data.Length }).ToListAsync(ct);
        var mode = rows.FirstOrDefault(r => r.Key == "mode")?.TextValue;
        if (mode is null || !Modes.Contains(mode)) mode = "computer";
        var bgs = rows.Where(r => r.Key.StartsWith("bg:") && r.Size > 0)
            .Select(r => new BackgroundInfo(r.Key[3..], r.Version, r.ContentType ?? "image/png", r.Size)).OrderBy(b => b.Kind).ToList();
        // a document is ON unless the Admin switched it off ("enabled:<kind>" = "0")
        var disabled = rows.Where(r => r.Key.StartsWith("enabled:") && r.TextValue == "0").Select(r => r.Key[8..]).Where(k => Kinds.Contains(k)).OrderBy(k => k).ToList();
        return Ok(new PrintConfigDto(mode, bgs, disabled));
    }

    [HttpGet("backgrounds/{kind}")]
    public async Task<IActionResult> GetBackground(string kind, CancellationToken ct)
    {
        if (!Kinds.Contains(kind)) return NotFound();
        var row = await db.PrintConfigs.AsNoTracking().SingleOrDefaultAsync(c => c.Key == "bg:" + kind, ct);
        if (row?.Data is not { Length: > 0 } data) return NotFound();
        Response.Headers.ETag = "\"" + row.Version + "\"";
        return File(data, row.ContentType ?? "image/png");
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("mode")]
    public async Task<IActionResult> SetMode(SetModeRequest request, CancellationToken ct)
    {
        var mode = (request.Mode ?? "").Trim().ToLowerInvariant();
        if (!Modes.Contains(mode)) return BadRequest(new { message = "Choose PDF, Native, Off, or let each computer decide." });
        var row = await db.PrintConfigs.SingleOrDefaultAsync(c => c.Key == "mode", ct);
        if (row is null) db.PrintConfigs.Add(row = new PrintConfig { Key = "mode" });
        row.TextValue = mode;
        row.Version++;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = CurrentUserId();
        AuditLogWriter.Add(db, AuditEntityType.Settings, Guid.Empty, "PrintModeChanged", CurrentUserId(), "Print mode: " + mode);
        await db.SaveChangesAsync(ct);
        return Ok(new { mode });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("enabled/{kind}")]
    public async Task<IActionResult> SetEnabled(string kind, SetEnabledRequest request, CancellationToken ct)
    {
        if (!Kinds.Contains(kind)) return NotFound(new { message = "Unknown document." });
        var key = "enabled:" + kind;
        var row = await db.PrintConfigs.SingleOrDefaultAsync(c => c.Key == key, ct);
        if (row is null) db.PrintConfigs.Add(row = new PrintConfig { Key = key });
        row.TextValue = request.Enabled ? "1" : "0";
        row.Version++;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = CurrentUserId();
        AuditLogWriter.Add(db, AuditEntityType.Settings, Guid.Empty, "PrintDocumentToggled", CurrentUserId(), $"{kind}: printing {(request.Enabled ? "on" : "off")}");
        await db.SaveChangesAsync(ct);
        return Ok(new { kind, enabled = request.Enabled });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("backgrounds/{kind}")]
    public async Task<IActionResult> SetBackground(string kind, SetBackgroundRequest request, CancellationToken ct)
    {
        if (!Kinds.Contains(kind)) return NotFound(new { message = "Unknown document." });
        byte[] data;
        try { data = Convert.FromBase64String(request.DataBase64 ?? ""); }
        catch (FormatException) { return BadRequest(new { message = "The picture could not be read." }); }
        if (data.Length == 0) return BadRequest(new { message = "The picture is empty." });
        if (data.Length > MaxBytes) return BadRequest(new { message = "The picture is too large. Use one under 6 MB." });
        // the real file type is read from the bytes, never trusted from the name
        string type;
        if (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) type = "image/png";
        else if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) type = "image/jpeg";
        else return BadRequest(new { message = "Only PNG or JPG pictures can be used." });

        var key = "bg:" + kind;
        var row = await db.PrintConfigs.SingleOrDefaultAsync(c => c.Key == key, ct);
        if (row is null) db.PrintConfigs.Add(row = new PrintConfig { Key = key });
        row.ContentType = type;
        row.Data = data;
        row.Version++;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = CurrentUserId();
        AuditLogWriter.Add(db, AuditEntityType.Settings, Guid.Empty, "PrintBackgroundSet", CurrentUserId(), $"{kind}: {type}, {data.Length / 1024} KB");
        await db.SaveChangesAsync(ct);
        return Ok(new BackgroundInfo(kind, row.Version, type, data.Length));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("backgrounds/{kind}")]
    public async Task<IActionResult> ClearBackground(string kind, CancellationToken ct)
    {
        if (!Kinds.Contains(kind)) return NotFound();
        var row = await db.PrintConfigs.SingleOrDefaultAsync(c => c.Key == "bg:" + kind, ct);
        if (row is { Data: not null })
        {
            // keep the row (version only ever goes up) so a computer that kept the old picture notices the change
            row.Data = null;
            row.ContentType = null;
            row.Version++;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedByUserId = CurrentUserId();
            AuditLogWriter.Add(db, AuditEntityType.Settings, Guid.Empty, "PrintBackgroundCleared", CurrentUserId(), kind);
            await db.SaveChangesAsync(ct);
        }
        return Ok(new { kind });
    }

    Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
//JJQR - END
