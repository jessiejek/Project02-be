using ClinicApp.Api.Hubs;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record CheckInWalkInRequest(
    Guid PatientId,
    VisitType? VisitType,
    bool? MedCertRequested,
    string? DiscountCategory,   // 'Senior' | 'PWD' | null
    string? Notes);

/// <summary>§16.3 — manual walk-in FCFS queue. Replaces the appointment-slot
/// model entirely: one doctor, no slots, patients are checked in in arrival
/// order and get a per-day queue number. A queue entry is still a `bookings`
/// row (is_walk_in = true) so the rest of the app — payments, consultation,
/// clinical records — is unchanged.</summary>
[ApiController]
[Authorize(Roles = "Admin,Staff,Doctor")]
[Route("api/queue")]
public class QueueController(ClinicAppDbContext db, IHubContext<ClinicHub> hub) : ControllerBase
{
    /// <summary>Check a walk-in patient into today's queue. Returns the printable ticket.</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPost]
    public async Task<ActionResult<object>> CheckIn(CheckInWalkInRequest req, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == req.PatientId, ct);
        if (patient is null) return BadRequest(new { message = "Patient not found." });

        // The clinic has exactly one doctor (owner-confirmed). Take it.
        var doctor = await db.Doctors.Include(d => d.StaffAccount)
            .OrderBy(d => d.DoctorId).FirstOrDefaultAsync(ct);
        if (doctor is null) return BadRequest(new { message = "No doctor configured." });

        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) return BadRequest(new { message = "Clinic settings missing." });

        var now = DateTimeOffset.UtcNow;
        var today = ClinicClock.Today;           // §17.1 #3 — Asia/Manila, not UTC
        var arrival = ClinicClock.TimeNow;
        var visitType = req.VisitType ?? VisitType.New;
        var discount = string.IsNullOrWhiteSpace(req.DiscountCategory) ? null : req.DiscountCategory.Trim();
        var medCert = req.MedCertRequested ?? false;
        var fee = ClinicFees.Compute(settings, visitType, medCert, discount);

        var seq = await NextQueueSeqAsync(today, ct);
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            PatientId = patient.PatientId,
            DoctorId = doctor.DoctorId,
            AppointmentDate = today,
            SlotStartTime = arrival,   // walk-in arrival time; slots are vestigial
            SlotEndTime = arrival,
            Status = BookingStatus.CheckedIn,
            PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = $"Q-{seq:D3}",
            VisitType = visitType,
            MedCertRequested = medCert,
            DiscountCategory = discount,
            DiscountAmount = fee.DiscountAmount,
            ConsultationFeeSnapshot = fee.Subtotal,
            TotalFee = fee.Total,
            AmountDue = fee.Total,
            IsWalkIn = true,
            Notes = req.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Bookings.Add(booking);
        db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            BookingId = booking.BookingId,
            Amount = fee.Total,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(ct);

        var patientName = $"{patient.FirstName} {patient.LastName}".Trim();
        await BroadcastAsync("PatientCheckedIn", doctor.DoctorId, new
        {
            booking_id = booking.BookingId,
            doctor_id = doctor.DoctorId,
            queue_number = booking.QueueNumber,
            patient_name = patientName,
            patient_code = patient.PatientCode
        });

        return Ok(new
        {
            booking_id = booking.BookingId,
            queue_number = booking.QueueNumber,
            sequence = seq,
            patient_name = patientName,
            patient_code = patient.PatientCode,
            doctor_name = doctor.StaffAccount?.FullName ?? "",
            clinic_name = settings.ClinicName,
            clinic_address = settings.Address,
            visit_type = visitType.ToString(),
            provisional_fee = fee.Total,
            issued_at = now
        });
    }

    /// <summary>Today's queue (or a given date), ordered FCFS, with a status summary.</summary>
    [HttpGet]
    public async Task<ActionResult<object>> GetQueue([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var day = date ?? ClinicApp.Domain.ClinicClock.Today;
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.AppointmentDate == day && b.IsWalkIn)
            .Include(b => b.Patient)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync(ct);

        var items = rows.Select(b => new
        {
            booking_id = b.BookingId,
            queue_number = b.QueueNumber,
            patient_id = b.PatientId,
            patient_name = b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            patient_code = b.Patient?.PatientCode ?? "",
            status = b.Status.ToString(),
            visit_type = b.VisitType.ToString(),
            amount_due = b.AmountDue,
            checked_in_at = b.CreatedAt
        }).ToList();

        return Ok(new
        {
            date = day,
            summary = new
            {
                waiting = rows.Count(b => b.Status is BookingStatus.CheckedIn or BookingStatus.OnHold),
                in_progress = rows.Count(b => b.Status == BookingStatus.InProgress),
                completed = rows.Count(b => b.Status == BookingStatus.Completed),
                no_show = rows.Count(b => b.Status == BookingStatus.NoShow),
                total = rows.Count
            },
            items
        });
    }

    [HttpPut("{bookingId:guid}/call")]
    public Task<IActionResult> Call(Guid bookingId, CancellationToken ct) => SetStatus(bookingId, BookingStatus.InProgress, ct);

    [HttpPut("{bookingId:guid}/hold")]
    public Task<IActionResult> Hold(Guid bookingId, CancellationToken ct) => SetStatus(bookingId, BookingStatus.OnHold, ct);

    [HttpPut("{bookingId:guid}/complete")]
    public Task<IActionResult> Complete(Guid bookingId, CancellationToken ct) => SetStatus(bookingId, BookingStatus.Completed, ct);

    [HttpPut("{bookingId:guid}/no-show")]
    public Task<IActionResult> NoShow(Guid bookingId, CancellationToken ct) => SetStatus(bookingId, BookingStatus.NoShow, ct);

    private async Task<IActionResult> SetStatus(Guid bookingId, BookingStatus status, CancellationToken ct)
    {
        var b = await db.Bookings.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return NotFound();
        b.Status = status;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await BroadcastAsync("QueueUpdated", b.DoctorId, new
        {
            booking_id = b.BookingId,
            doctor_id = b.DoctorId,
            status = b.Status.ToString()
        });

        return Ok(b);
    }

    /// <summary>Every real-time push goes to "staff" plus the owning doctor's
    /// own group — one call site instead of repeating both SendAsync calls
    /// at every mutation.</summary>
    private Task BroadcastAsync(string @event, Guid doctorId, object payload) =>
        Task.WhenAll(
            hub.Clients.Group("staff").SendAsync(@event, payload),
            hub.Clients.Group($"doctor:{doctorId}").SendAsync(@event, payload));

    /// <summary>Next FCFS sequence for the day — MAX(existing Q-NNN) + 1, so a
    /// cancelled entry never causes a duplicate.</summary>
    private async Task<int> NextQueueSeqAsync(DateOnly day, CancellationToken ct)
    {
        var numbers = await db.Bookings.AsNoTracking()
            .Where(b => b.AppointmentDate == day && b.QueueNumber != null)
            .Select(b => b.QueueNumber!)
            .ToListAsync(ct);
        var max = 0;
        foreach (var n in numbers)
        {
            var digits = new string(n.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var v) && v > max) max = v;
        }
        return max + 1;
    }
}
