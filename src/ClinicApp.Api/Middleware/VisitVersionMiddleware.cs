//JJQR - AI (Nov 22, 2026) - stale-form protection for a visit. A visit is saved through several endpoints (consultation, vitals, prescriptions, labs, follow-up, diagnoses),
// and a form left open on one computer used to overwrite what another computer had saved since, with no warning (QA critical #1).
// Every read of those endpoints returns an X-Visit-Version (a fingerprint of the whole visit's saved state). A client sends the version it last saw on every write;
// if the visit changed in between, the write is refused with 409 { code: "visit_changed" } before anything is touched, and the client has to reload.
// A write without the header behaves as before (older clients keep working). After a successful write the new version is returned in the response header.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Middleware;

public sealed partial class VisitVersionMiddleware(RequestDelegate next)
{
    public const string VersionHeader = "X-Visit-Version";
    public const string BookingHeader = "X-Visit-Booking";
    public const string ConsultationHeader = "X-Visit-Consultation";
    // One write at a time per visit: two saves of the same visit can no longer interleave (the stale-version check and the save are one step, and a double-click cannot create duplicates).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    public const string ConflictMessage = "This visit was changed on another computer or window since you opened it. Reload the visit to see the latest, then make your change again.";

    [GeneratedRegex(@"^/api/(?:consultations|vitals|prescription-groups|lab-orders)/by-booking/(?<id>[0-9a-fA-F-]{36})/?$")]
    private static partial Regex ByBooking();
    [GeneratedRegex(@"^/api/consultations/(?<id>[0-9a-fA-F-]{36})/diagnoses/?$")]
    private static partial Regex Diagnoses();
    [GeneratedRegex(@"^/api/(?:lab-orders|follow-ups)/by-consultation/(?<id>[0-9a-fA-F-]{36})/?$")]
    private static partial Regex ByConsultation();
    [GeneratedRegex(@"^/api/prescription-groups/(?<id>[0-9a-fA-F-]{36})/?$")]
    private static partial Regex RxGroup();

    public async Task InvokeAsync(HttpContext context, ClinicAppDbContext db)
    {
        var method = context.Request.Method;
        var isWrite = HttpMethods.IsPut(method) || HttpMethods.IsDelete(method);
        if (!(isWrite || HttpMethods.IsGet(method)) || context.Request.Path.Value is not { } path || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var ct = context.RequestAborted;
        var (bookingId, consultationId) = await ResolveAsync(db, context.Request, path, ct);
        if (bookingId is null)
        {
            await next(context);
            return;
        }

        var booking = bookingId.Value;
        SemaphoreSlim? gate = null;
        if (isWrite)
        {
            gate = Locks.GetOrAdd(booking, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
        }
        try
        {
            if (isWrite && context.Request.Headers.TryGetValue(VersionHeader, out var sent) && !string.IsNullOrWhiteSpace(sent))
            {
                var current = await ComputeAsync(db, booking, ct);
                if (!string.Equals(sent.ToString().Trim(), current, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    context.Response.Headers[VersionHeader] = current;
                    context.Response.Headers[BookingHeader] = booking.ToString("D");
                    await context.Response.WriteAsJsonAsync(new { code = "visit_changed", message = ConflictMessage, latest_version = current }, ct);
                    return;
                }
            }

            context.Response.OnStarting(async () =>
            {
                var status = context.Response.StatusCode;
                if (status >= 400 && status != StatusCodes.Status404NotFound) return; // errors (incl. our own 409, already stamped) carry no version
                if (context.Response.Headers.ContainsKey(VersionHeader)) return;
                try
                {
                    context.Response.Headers[VersionHeader] = await ComputeAsync(db, booking, CancellationToken.None);
                    context.Response.Headers[BookingHeader] = booking.ToString("D");
                    if (consultationId is not null) context.Response.Headers[ConsultationHeader] = consultationId.Value.ToString("D");
                    else if (await db.Consultations.AsNoTracking().Where(c => c.BookingId == booking).Select(c => (Guid?)c.ConsultationId).FirstOrDefaultAsync() is { } cid)
                        context.Response.Headers[ConsultationHeader] = cid.ToString("D");
                }
                catch { /* a version header must never break the real answer */ }
            });

            await next(context);
        }
        finally
        {
            gate?.Release();
        }
    }

    private static async Task<(Guid? Booking, Guid? Consultation)> ResolveAsync(ClinicAppDbContext db, HttpRequest req, string path, CancellationToken ct)
    {
        var q = req.Query;
        if (ByBooking().Match(path) is { Success: true } m1 && Guid.TryParse(m1.Groups["id"].Value, out var b1)) return (b1, null);
        if (Diagnoses().Match(path) is { Success: true } m2 && Guid.TryParse(m2.Groups["id"].Value, out var c2)) return await FromConsultationAsync(db, c2, ct);
        if (ByConsultation().Match(path) is { Success: true } m3 && Guid.TryParse(m3.Groups["id"].Value, out var c3)) return await FromConsultationAsync(db, c3, ct);
        if (RxGroup().Match(path) is { Success: true } m4 && Guid.TryParse(m4.Groups["id"].Value, out var g4))
        {
            var b = await db.PrescriptionGroups.AsNoTracking().Where(g => g.GroupId == g4).Select(g => (Guid?)g.BookingId).FirstOrDefaultAsync(ct);
            return (b, null);
        }
        if (path.Equals("/api/vitals", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/prescription-groups", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/consultations", StringComparison.OrdinalIgnoreCase))
            if (Guid.TryParse(q["bookingId"].ToString(), out var qb)) return (qb, null);
        if (path.Equals("/api/follow-ups", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(q["consultationId"].ToString(), out var qc))
            return await FromConsultationAsync(db, qc, ct);
        return (null, null);
    }

    private static async Task<(Guid? Booking, Guid? Consultation)> FromConsultationAsync(ClinicAppDbContext db, Guid consultationId, CancellationToken ct)
    {
        var b = await db.Consultations.AsNoTracking().Where(c => c.ConsultationId == consultationId).Select(c => (Guid?)c.BookingId).FirstOrDefaultAsync(ct);
        return (b, b is null ? null : consultationId);
    }

    /// <summary>A short fingerprint of everything saved for the visit. Any add, change or removal of a part changes it.</summary>
    public static async Task<string> ComputeAsync(ClinicAppDbContext db, Guid bookingId, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var c = await db.Consultations.AsNoTracking().Where(x => x.BookingId == bookingId)
            .Select(x => new { x.ConsultationId, x.UpdatedAt, x.Status }).FirstOrDefaultAsync(ct);
        sb.Append("c:").Append(c is null ? "-" : $"{c.ConsultationId:N}.{c.UpdatedAt.UtcTicks}.{(int)c.Status}");

        var vitals = await db.PatientVitalReadings.AsNoTracking().Where(r => r.BookingId == bookingId).OrderBy(r => r.TemplateId)
            .Select(r => new { r.TemplateId, r.UpdatedAt }).ToListAsync(ct);
        sb.Append("|v:").AppendJoin(',', vitals.Select(v => $"{v.TemplateId:N}.{v.UpdatedAt.UtcTicks}"));

        var rx = await db.PrescriptionGroups.AsNoTracking().Where(g => g.BookingId == bookingId).OrderBy(g => g.GroupId)
            .Select(g => new { g.GroupId, g.UpdatedAt, N = g.LineItems.Count }).ToListAsync(ct);
        sb.Append("|r:").AppendJoin(',', rx.Select(g => $"{g.GroupId:N}.{g.UpdatedAt.UtcTicks}.{g.N}"));

        if (c is not null)
        {
            var labs = await db.LabOrders.AsNoTracking().Where(l => l.ConsultationId == c.ConsultationId).OrderBy(l => l.LabOrderId)
                .Select(l => new { l.LabOrderId, l.UpdatedAt }).ToListAsync(ct);
            sb.Append("|l:").AppendJoin(',', labs.Select(l => $"{l.LabOrderId:N}.{l.UpdatedAt.UtcTicks}"));

            var fu = await db.FollowUps.AsNoTracking().Where(f => f.ConsultationId == c.ConsultationId)
                .Select(f => new { f.Id, f.UpdatedAt }).ToListAsync(ct);
            sb.Append("|f:").AppendJoin(',', fu.Select(f => $"{f.Id:N}.{f.UpdatedAt.UtcTicks}"));

            var dx = await db.ConsultationDiagnoses.AsNoTracking().Where(d => d.ConsultationId == c.ConsultationId).OrderBy(d => d.Id)
                .Select(d => d.Id).ToListAsync(ct);
            sb.Append("|d:").AppendJoin(',', dx.Select(i => i.ToString("N")));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())), 0, 8).ToLowerInvariant();
    }
}
//JJQR - END
