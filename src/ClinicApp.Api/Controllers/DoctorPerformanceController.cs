//JJQR - AI (Nov 19, 2026) - NEW: GET api/doctor/performance — one aggregated, doctor-scoped performance read (patients, money, trends, 14-day series, top diagnoses, unpaid + follow-up lists).
using ClinicApp.Api.Security;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>Numbers for ONE period. Money is gross (no clinic share), PHP, and only counts COMPLETED visits
/// dated (Manila) inside the period. Collected = Paid payments (amount_received, else amount); a Refunded
/// payment is never collected; a paid-but-not-yet-completed booking is excluded entirely.</summary>
public record PerformanceStats(
    int PatientsSeen, int UniquePatients, int NewVisits, int FollowUpVisits, int FirstTimePatients,
    decimal Collected, decimal Billed, decimal Waived, decimal Refunded, decimal UnpaidTotal,
    int RxGroups, int MedCerts, int LabOrders, int NoShows, int Cancellations,
    double? AvgConsultMinutes, int AvgConsultSamples);

public record PerformancePeriod(string Name, DateOnly From, DateOnly To);
public record PerformanceDay(DateOnly Date, int PatientsSeen, decimal Collected, decimal Billed);
public record TopDiagnosis(string? Icd10Code, string Label, int Count);
public record UnpaidVisit(Guid BookingId, Guid PatientId, string PatientName, DateOnly AppointmentDate, decimal AmountDue);
public record UnpaidSummary(int Count, decimal Total, List<UnpaidVisit> Items);
public record DueFollowUp(Guid FollowUpId, Guid PatientId, string PatientName, DateOnly FollowUpDate, string? Reason);
public record FollowUpSummary(int Count, List<DueFollowUp> Items);

public record DoctorPerformanceResponse(
    Guid DoctorId, string Currency, string WeekStartsOn, PerformancePeriod Period, PerformancePeriod PreviousPeriod,
    PerformanceStats Current, PerformanceStats Previous, List<PerformanceDay> Series,
    List<TopDiagnosis> TopDiagnoses, UnpaidSummary Unpaid, FollowUpSummary FollowUpsDue);

/// <summary>Doctor → their own numbers only (the doctor id is taken from the login; any ?doctorId= is ignored).
/// Admin → must pass ?doctorId=. Staff / Patient → 403 (role gate). Anonymous → 401.</summary>
[ApiController]
[Authorize(Roles = "Doctor,Admin")]
[Route("api/doctor/performance")]
public class DoctorPerformanceController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    public const int SeriesDays = 14;
    public const int ListLimit = 20;
    public const int TopDiagnosisLimit = 5;
    public const int MinConsultSamples = 5;
    /// <summary>A "consult" longer than this is a draft that was left open, not a real visit length.</summary>
    public const int MaxConsultMinutes = 180;

    [HttpGet]
    public async Task<ActionResult<DoctorPerformanceResponse>> Get([FromQuery] string? period, [FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        Guid doctor;
        if (actor.IsDoctor)
        {
            if (actor.StaffId is not { } own) return Forbid();
            doctor = own; // a doctor can only ever see themself — whatever ?doctorId= says
        }
        else if (actor.IsAdmin)
        {
            if (doctorId is null) return BadRequest(new { message = "doctorId is required for admins." });
            if (!await db.Doctors.AnyAsync(d => d.DoctorId == doctorId, ct)) return NotFound();
            doctor = doctorId.Value;
        }
        else return Forbid();

        var name = (period ?? "today").Trim().ToLowerInvariant();
        var today = ClinicClock.Today;
        if (!TryRange(name, today, out var cur, out var prev)) return BadRequest(new { message = "period must be today, week or month." });

        var current = await StatsAsync(doctor, cur.From, cur.To, ct);
        var previous = await StatsAsync(doctor, prev.From, prev.To, ct);
        var series = await SeriesAsync(doctor, today, ct);
        var top = await TopDiagnosesAsync(doctor, cur.From, cur.To, ct);
        var unpaid = await UnpaidAsync(doctor, ct);
        var followUps = await FollowUpsAsync(doctor, today, ct);

        return Ok(new DoctorPerformanceResponse(doctor, "PHP", "Monday", cur, prev, current, previous, series, top, unpaid, followUps));
    }

    // ── period boundaries (Manila days; week = Monday..today; month = 1st..today) ──────────
    /// <summary>The previous period spans the same number of elapsed days, so "this week so far" is compared
    /// with "last week to the same weekday" and a trend never looks bad just because the period is young.</summary>
    public static bool TryRange(string name, DateOnly today, out PerformancePeriod current, out PerformancePeriod previous)
    {
        current = previous = null!;
        switch (name)
        {
            case "today":
                current = new("today", today, today);
                previous = new("yesterday", today.AddDays(-1), today.AddDays(-1));
                return true;
            case "week":
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                current = new("week", monday, today);
                previous = new("previous_week", monday.AddDays(-7), today.AddDays(-7));
                return true;
            case "month":
                var first = new DateOnly(today.Year, today.Month, 1);
                var prevFirst = first.AddMonths(-1);
                var prevLast = first.AddDays(-1);
                var prevTo = new DateOnly(prevFirst.Year, prevFirst.Month, Math.Min(today.Day, prevLast.Day));
                current = new("month", first, today);
                previous = new("previous_month", prevFirst, prevTo);
                return true;
            default:
                return false;
        }
    }

    private sealed record VisitRow(Guid BookingId, Guid PatientId, DateOnly Date, VisitType VisitType, decimal TotalFee, decimal AmountDue,
        PaymentStatus? PayStatus, decimal? PayAmount, decimal? PayReceived, decimal? PayRefund);

    private async Task<List<VisitRow>> CompletedVisitsAsync(Guid doctor, DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.Status == BookingStatus.Completed && b.AppointmentDate >= from && b.AppointmentDate <= to)
            .Select(b => new VisitRow(b.BookingId, b.PatientId, b.AppointmentDate, b.VisitType, b.TotalFee, b.AmountDue,
                b.Payment == null ? null : b.Payment.Status, b.Payment == null ? null : b.Payment.Amount,
                b.Payment == null ? null : b.Payment.AmountReceived, b.Payment == null ? null : b.Payment.RefundAmount))
            .ToListAsync(ct);

    private static decimal CollectedOf(VisitRow v) => v.PayStatus == PaymentStatus.Paid ? v.PayReceived ?? v.PayAmount ?? 0m : 0m;

    private async Task<PerformanceStats> StatsAsync(Guid doctor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var visits = await CompletedVisitsAsync(doctor, from, to, ct);
        var patients = visits.Select(v => v.PatientId).Distinct().ToList();

        // first-time = no completed visit with this doctor before the period starts
        var returning = patients.Count == 0 ? new HashSet<Guid>() : (await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.Status == BookingStatus.Completed && b.AppointmentDate < from && patients.Contains(b.PatientId))
            .Select(b => b.PatientId).Distinct().ToListAsync(ct)).ToHashSet();

        var statuses = await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.AppointmentDate >= from && b.AppointmentDate <= to
                && (b.Status == BookingStatus.NoShow || b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Expired))
            .Select(b => b.Status).ToListAsync(ct);

        var rx = await db.PrescriptionGroups.AsNoTracking()
            .CountAsync(g => g.DoctorId == doctor && g.Booking != null && g.Booking.AppointmentDate >= from && g.Booking.AppointmentDate <= to, ct);
        var certs = await db.MedicalCertificates.AsNoTracking()
            .CountAsync(m => m.DoctorId == doctor && m.IssueDate >= from && m.IssueDate <= to, ct);
        var labs = await db.LabOrders.AsNoTracking()
            .CountAsync(l => l.DoctorId == doctor && db.Consultations.Any(c => c.ConsultationId == l.ConsultationId
                && c.Booking != null && c.Booking.AppointmentDate >= from && c.Booking.AppointmentDate <= to), ct);

        var spans = await db.Consultations.AsNoTracking()
            .Where(c => c.DoctorId == doctor && c.CompletedAt != null && (c.Status == ConsultationStatus.Completed || c.Status == ConsultationStatus.Amended)
                && c.Booking != null && c.Booking.AppointmentDate >= from && c.Booking.AppointmentDate <= to)
            .Select(c => new { c.CreatedAt, c.CompletedAt }).ToListAsync(ct);
        var minutes = spans.Select(s => (s.CompletedAt!.Value - s.CreatedAt).TotalMinutes).Where(m => m > 0 && m <= MaxConsultMinutes).ToList();

        return new PerformanceStats(
            PatientsSeen: visits.Count,
            UniquePatients: patients.Count,
            NewVisits: visits.Count(v => v.VisitType == VisitType.New),
            FollowUpVisits: visits.Count(v => v.VisitType == VisitType.FollowUp),
            FirstTimePatients: patients.Count(p => !returning.Contains(p)),
            Collected: visits.Sum(CollectedOf),
            Billed: visits.Sum(v => v.TotalFee),
            Waived: visits.Where(v => v.PayStatus == PaymentStatus.Waived).Sum(v => v.TotalFee),
            Refunded: visits.Where(v => v.PayStatus == PaymentStatus.Refunded).Sum(v => v.PayRefund ?? v.PayReceived ?? v.PayAmount ?? 0m),
            UnpaidTotal: visits.Where(v => v.AmountDue > 0).Sum(v => v.AmountDue),
            RxGroups: rx, MedCerts: certs, LabOrders: labs,
            NoShows: statuses.Count(s => s == BookingStatus.NoShow),
            Cancellations: statuses.Count(s => s is BookingStatus.Cancelled or BookingStatus.Expired),
            AvgConsultMinutes: minutes.Count >= MinConsultSamples ? Math.Round(minutes.Average(), 1) : null,
            AvgConsultSamples: minutes.Count);
    }

    /// <summary>Last 14 Manila days ending today, every day present (zero-filled).</summary>
    private async Task<List<PerformanceDay>> SeriesAsync(Guid doctor, DateOnly today, CancellationToken ct)
    {
        var start = today.AddDays(-(SeriesDays - 1));
        var visits = await CompletedVisitsAsync(doctor, start, today, ct);
        return Enumerable.Range(0, SeriesDays).Select(i =>
        {
            var d = start.AddDays(i);
            var day = visits.Where(v => v.Date == d).ToList();
            return new PerformanceDay(d, day.Count, day.Sum(CollectedOf), day.Sum(v => v.TotalFee));
        }).ToList();
    }

    private async Task<List<TopDiagnosis>> TopDiagnosesAsync(Guid doctor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.ConsultationDiagnoses.AsNoTracking()
            .Where(d => d.Type != DiagnosisType.Differential
                && db.Consultations.Any(c => c.ConsultationId == d.ConsultationId && c.DoctorId == doctor
                    && (c.Status == ConsultationStatus.Completed || c.Status == ConsultationStatus.Amended)
                    && c.Booking != null && c.Booking.AppointmentDate >= from && c.Booking.AppointmentDate <= to))
            .Select(d => new { d.ConsultationId, d.Icd10Code, d.CustomDescription }).ToListAsync(ct);

        var codes = rows.Where(r => !string.IsNullOrWhiteSpace(r.Icd10Code)).Select(r => r.Icd10Code!).Distinct().ToList();
        var names = codes.Count == 0 ? new Dictionary<string, string>() :
            await db.Icd10Codes.AsNoTracking().Where(c => codes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, c => c.Description, ct);

        return rows
            .Select(r => new
            {
                r.ConsultationId,
                Code = string.IsNullOrWhiteSpace(r.Icd10Code) ? null : r.Icd10Code!.Trim(),
                Text = (r.CustomDescription ?? "").Trim()
            })
            .Where(r => r.Code is not null || r.Text.Length > 0)
            .Select(r => new { r.ConsultationId, r.Code, Key = r.Code ?? r.Text.ToLowerInvariant(), Label = r.Code is not null ? names.GetValueOrDefault(r.Code, r.Text.Length > 0 ? r.Text : r.Code) : r.Text })
            .DistinctBy(r => (r.ConsultationId, r.Key)) // one visit counts a diagnosis once
            .GroupBy(r => r.Key)
            .Select(g => new TopDiagnosis(g.First().Code, g.First().Label, g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Label)
            .Take(TopDiagnosisLimit).ToList();
    }

    /// <summary>Everything this doctor still has outstanding (completed visits with money due), newest first.</summary>
    private async Task<UnpaidSummary> UnpaidAsync(Guid doctor, CancellationToken ct)
    {
        var due = await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.Status == BookingStatus.Completed && b.AmountDue > 0)
            .Select(b => new { b.BookingId, b.PatientId, b.AppointmentDate, b.AmountDue, First = b.Patient.FirstName, Last = b.Patient.LastName })
            .ToListAsync(ct);
        var items = due.OrderByDescending(b => b.AppointmentDate).ThenBy(b => b.Last).Take(ListLimit)
            .Select(b => new UnpaidVisit(b.BookingId, b.PatientId, $"{b.First} {b.Last}".Trim(), b.AppointmentDate, b.AmountDue)).ToList();
        return new UnpaidSummary(due.Count, due.Sum(b => b.AmountDue), items);
    }

    private async Task<FollowUpSummary> FollowUpsAsync(Guid doctor, DateOnly today, CancellationToken ct)
    {
        var until = today.AddDays(7);
        var rows = await db.FollowUps.AsNoTracking()
            .Where(f => f.DoctorId == doctor && f.Status == FollowUpStatus.Pending && f.FollowUpDate >= today && f.FollowUpDate <= until)
            .Select(f => new { f.Id, f.PatientId, f.FollowUpDate, f.Reason, First = db.Patients.Where(p => p.PatientId == f.PatientId).Select(p => p.FirstName).FirstOrDefault(), Last = db.Patients.Where(p => p.PatientId == f.PatientId).Select(p => p.LastName).FirstOrDefault() })
            .ToListAsync(ct);
        var items = rows.OrderBy(f => f.FollowUpDate).ThenBy(f => f.Last).Take(ListLimit)
            .Select(f => new DueFollowUp(f.Id, f.PatientId, $"{f.First} {f.Last}".Trim(), f.FollowUpDate, f.Reason)).ToList();
        return new FollowUpSummary(rows.Count, items);
    }
}
//JJQR - END
