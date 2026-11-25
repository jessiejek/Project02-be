//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Hubs;
using ClinicApp.Api.Security;
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
    string? Notes,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? BookingId = null,
    Guid? PaymentId = null
    //JJQR - END
    );

public record CancelQueueEntryRequest(string? Reason);

/// <summary>§16.3 — manual walk-in FCFS queue. Replaces the appointment-slot
/// model entirely: one doctor, no slots, patients are checked in in arrival
/// order and get a per-day queue number. A queue entry is still a `bookings`
/// row (is_walk_in = true) so the rest of the app — payments, consultation,
/// clinical records — is unchanged.</summary>
[ApiController]
[Authorize(Roles = "Admin,Staff,Doctor")]
[Route("api/queue")]
public class QueueController(ClinicAppDbContext db, IHubContext<ClinicHub> hub, ActorResolver actors) : ControllerBase
{
    /// <summary>Check a walk-in patient into today's queue. Returns the printable ticket.</summary>
    //JJQR - AI (Nov 22, 2026) - the doctor portal has "New walk-in consult"; it was refused (403) because only Admin/Staff could check a patient in //JJQR - END
    [Authorize(Roles = "Admin,Staff,Doctor")]
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

        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (ClientIds.Supplied(req.BookingId))
        {
            var dup = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == req.BookingId, ct);
            if (dup is not null)
            {
                if (dup.PatientId != patient.PatientId) return ClientIds.Conflict();
                var qn = dup.QueueNumber ?? "";
                return Ok(new
                {
                    booking_id = dup.BookingId,
                    queue_number = dup.QueueNumber,
                    lane = QueueLane.IsPriority(qn) ? QueueLane.Priority : QueueLane.Regular,
                    is_priority = QueueLane.IsPriority(qn),
                    sequence = int.TryParse(new string(qn.Where(char.IsDigit).ToArray()), out var seqNo) ? seqNo : 0,
                    patient_name = $"{patient.FirstName} {patient.LastName}".Trim(),
                    patient_code = patient.PatientCode,
                    doctor_name = doctor.StaffAccount?.FullName ?? "",
                    clinic_name = settings.ClinicName,
                    clinic_address = settings.Address,
                    visit_type = dup.VisitType.ToString(),
                    provisional_fee = dup.TotalFee,
                    issued_at = dup.CreatedAt
                });
            }
        }
        if (await ClientIds.TakenAsync<Payment>(db, req.PaymentId, ct)) return ClientIds.Conflict();
        //JJQR - END

        var now = DateTimeOffset.UtcNow;
        var today = ClinicClock.Today;           // §17.1 #3 — Asia/Manila, not UTC
        var arrival = ClinicClock.TimeNow;
        var visitType = req.VisitType ?? VisitType.New;
        //JJQR - AI (Nov 21, 2026) - only "Senior" or "PWD": any other text used to grant the discount AND the priority lane
        if (!QueueLane.IsValidDiscount(req.DiscountCategory))
            return BadRequest(new { message = "Discount must be Senior or PWD." });
        var discount = QueueLane.NormalizeDiscount(req.DiscountCategory);
        //JJQR - END
        var medCert = req.MedCertRequested ?? false;
        var fee = ClinicFees.Compute(settings, visitType, medCert, discount);

        //JJQR - AI (Nov 21, 2026) - priority lane: Senior/PWD (discount picked, 60+, or ID on file) get an S- number, everyone else R-
        var queueNumber = await QueueSequencer.NextForPatientAsync(db, today, patient.PatientId, discount, ct);
        //JJQR - END
        var booking = new Booking
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            BookingId = ClientIds.Use(req.BookingId),
            //JJQR - END
            PatientId = patient.PatientId,
            DoctorId = doctor.DoctorId,
            AppointmentDate = today,
            SlotStartTime = arrival,   // walk-in arrival time; slots are vestigial
            SlotEndTime = arrival,
            Status = BookingStatus.CheckedIn,
            PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = queueNumber,
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
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            PaymentId = ClientIds.Use(req.PaymentId),
            //JJQR - END
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
            //JJQR - AI (Nov 21, 2026) - lane for the ticket / slip
            lane = QueueLane.IsPriority(queueNumber) ? QueueLane.Priority : QueueLane.Regular,
            is_priority = QueueLane.IsPriority(queueNumber),
            //JJQR - END
            sequence = int.Parse(new string(queueNumber.Where(char.IsDigit).ToArray())),
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
            .Where(b => b.AppointmentDate == day && b.QueueNumber != null
                && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .Include(b => b.Patient)
            //JJQR - AI (Nov 21, 2026) - seniors / PWD (S-) first, then first-come-first-served within each lane
            .OrderBy(b => b.QueueNumber!.StartsWith(QueueLane.PriorityPrefix) ? 0 : 1)
            .ThenBy(b => b.CreatedAt)
            //JJQR - END
            .ToListAsync(ct);

        var items = rows.Select(b => new
        {
            booking_id = b.BookingId,
            queue_number = b.QueueNumber,
            is_priority = QueueLane.IsPriority(b.QueueNumber), //JJQR - AI (Nov 21, 2026) - priority lane
            patient_id = b.PatientId,
            patient_name = b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            patient_code = b.Patient?.PatientCode ?? "",
            status = b.Status.ToString(),
            visit_type = b.VisitType.ToString(),
            amount_due = b.AmountDue,
            checked_in_at = b.CreatedAt,
            is_walk_in = b.IsWalkIn
        }).ToList();

        return Ok(new
        {
            date = day,
            summary = new
            {
                booked = rows.Count(b => b.Status == BookingStatus.Pending),
                waiting = rows.Count(b => b.Status is BookingStatus.CheckedIn or BookingStatus.OnHold),
                in_progress = rows.Count(b => b.Status == BookingStatus.InProgress),
                completed = rows.Count(b => b.Status == BookingStatus.Completed),
                no_show = rows.Count(b => b.Status == BookingStatus.NoShow),
                total = rows.Count
            },
            items
        });
    }

    //JJQR - AI (2026-11-25) - why: GAV-4 (owner approved the "Not finished on an earlier day" mockup). A walk-in left Waiting / On hold / In
    // consultation when the day ended used to vanish from every Today screen at midnight (the board above is today only) and stayed stuck forever.
    // This read lists them: live status, a date BEFORE today (Manila), last LeftoverDaysBack days. Scope = the caller's existing today queue: a doctor
    // sees their own, staff / admin see the whole clinic. Read only, no schema change.
    public const int LeftoverDaysBack = 30;
    private static readonly BookingStatus[] LeftoverStatuses = [BookingStatus.CheckedIn, BookingStatus.OnHold, BookingStatus.InProgress];

    [HttpGet("leftovers")]
    public async Task<ActionResult<object>> GetLeftovers(CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor && actor.StaffId is null) return Forbid();
        var today = ClinicApp.Domain.ClinicClock.Today;
        var from = today.AddDays(-LeftoverDaysBack);
        var query = db.Bookings.AsNoTracking()
            .Where(b => b.AppointmentDate < today && b.AppointmentDate >= from && b.QueueNumber != null
                && (b.Status == BookingStatus.CheckedIn || b.Status == BookingStatus.OnHold || b.Status == BookingStatus.InProgress));
        if (actor.IsDoctor) query = query.Where(b => b.DoctorId == actor.StaffId);
        var rows = await query.Include(b => b.Patient).ToListAsync(ct);
        var items = rows
            .OrderByDescending(b => b.AppointmentDate)
            .ThenBy(b => QueueLane.IsPriority(b.QueueNumber) ? 0 : 1)
            .ThenBy(b => b.CreatedAt)
            .Select(b => new
            {
                booking_id = b.BookingId,
                appointment_date = b.AppointmentDate,
                queue_number = b.QueueNumber,
                is_priority = QueueLane.IsPriority(b.QueueNumber),
                patient_id = b.PatientId,
                patient_name = b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
                patient_code = b.Patient?.PatientCode ?? "",
                doctor_id = b.DoctorId,
                status = b.Status.ToString(),
                visit_type = b.VisitType.ToString(),
                amount_due = b.AmountDue,
                checked_in_at = b.CreatedAt,
                updated_at = b.UpdatedAt,
                is_walk_in = b.IsWalkIn
            }).ToList();
        return Ok(new { today, from, items });
    }

    // "Move to today": the SAME booking row (its consultation, payment and files stay linked) gets today's date and a NEW queue number for today
    // (same lane), keeps its status, and an audit row records the old date / number. Race guard: inside one transaction the row is CLAIMED with a
    // conditional UPDATE (same date, status and row_version we read, like payments' ClaimUnpaid); whoever loses gets 409 and nothing changes.
    // Offline sync: online-only action (the desktop refuses it offline); the server change bumps row_version once, so nodes pull it, and the sync push
    // engine no longer lets a node that never saw this change put the old date back (SyncPushEngine, appointment_date rule).
    public const string LeftoverChanged = "This patient's visit just changed (moved, completed or marked no-show). The list has been refreshed.";

    [HttpPut("{bookingId:guid}/move-to-today")]
    public async Task<IActionResult> MoveToToday(Guid bookingId, CancellationToken ct)
    {
        var seen = await db.Bookings.AsNoTracking().Include(b => b.Patient).SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (seen is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor && !actor.ActsAsDoctor(seen.DoctorId)) return Forbid();
        var today = ClinicApp.Domain.ClinicClock.Today;
        if (seen.AppointmentDate == today && LeftoverStatuses.Contains(seen.Status))
            return Conflict(new { error = "already_today", message = "This patient is already on today's queue.", queue_number = seen.QueueNumber });
        if (!LeftoverStatuses.Contains(seen.Status) || seen.AppointmentDate > today)
            return Conflict(new { error = "not_leftover", message = LeftoverChanged });
        var name = seen.Patient is null ? "This patient" : $"{seen.Patient.FirstName} {seen.Patient.LastName}".Trim();
        var other = await db.Bookings.AsNoTracking()
            .Where(b => b.PatientId == seen.PatientId && b.BookingId != seen.BookingId && b.AppointmentDate == today
                && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn
                    || b.Status == BookingStatus.OnHold || b.Status == BookingStatus.InProgress))
            .Select(b => new { b.QueueNumber }).FirstOrDefaultAsync(ct);
        if (other is not null)
            return Conflict(new { error = "already_booked_today", message = $"{name} is already on today's list{(other.QueueNumber is null ? "" : $" as {other.QueueNumber}")}. Mark the earlier visit no-show instead." });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Bookings
            .Where(b => b.BookingId == bookingId && b.RowVersion == seen.RowVersion && b.AppointmentDate == seen.AppointmentDate && b.Status == seen.Status)
            .ExecuteUpdateAsync(set => set.SetProperty(b => b.RowVersion, b => b.RowVersion + 1), ct);
        if (claimed != 1) return Conflict(new { error = "not_leftover", message = LeftoverChanged });

        var newNumber = await QueueSequencer.NextForPatientAsync(db, today, seen.PatientId, seen.DiscountCategory, ct);
        db.ChangeTracker.Clear();
        var booking = await db.Bookings.SingleAsync(b => b.BookingId == bookingId, ct);
        booking.RowVersion = seen.RowVersion; // the save below adds exactly 1 (like any other save), the claim above already wrote that same value
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (Guid?)null;
        booking.AppointmentDate = today;
        booking.QueueNumber = newNumber;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId, "Moved to today", userId,
            $"Left over from {seen.AppointmentDate:yyyy-MM-dd} as {seen.QueueNumber} ({seen.Status}); now {today:yyyy-MM-dd} as {newNumber}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await BroadcastAsync("QueueUpdated", booking.DoctorId, new { booking_id = booking.BookingId, doctor_id = booking.DoctorId, status = booking.Status.ToString() });
        return Ok(new
        {
            booking_id = booking.BookingId,
            appointment_date = booking.AppointmentDate,
            queue_number = booking.QueueNumber,
            status = booking.Status.ToString(),
            previous_date = seen.AppointmentDate,
            previous_queue_number = seen.QueueNumber
        });
    }
    //JJQR - END

    /// <summary>Front desk marks an online booking as arrived (Pending → CheckedIn).</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPut("{bookingId:guid}/check-in")]
    public async Task<IActionResult> CheckInBooked(Guid bookingId, CancellationToken ct)
    {
        var b = await db.Bookings.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return NotFound();
        if (b.Status != BookingStatus.Pending)
            return BadRequest(new { message = "Only a pending online booking can be checked in." });
        return await SetStatus(bookingId, BookingStatus.CheckedIn, ct);
    }

    /// <summary>Front desk cancels a booking that hasn't been seen yet (Pending / CheckedIn /
    /// OnHold) — e.g. the patient left or changed their mind after checking in. A reason is
    /// required for the audit trail. The row is kept as Cancelled (never deleted), and a booking
    /// that's already been paid is refused so money can't silently go missing.</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPut("{bookingId:guid}/cancel")]
    public async Task<IActionResult> CancelEntry(Guid bookingId, CancelQueueEntryRequest req, CancellationToken ct)
    {
        var reason = req.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { message = "A cancellation reason is required." });

        var b = await db.Bookings.Include(x => x.Payment).SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return NotFound();
        if (b.Status is not (BookingStatus.Pending or BookingStatus.CheckedIn or BookingStatus.OnHold))
            return BadRequest(new { message = "Only a booking that hasn't been seen yet can be cancelled." });
        if (b.Payment?.Status == PaymentStatus.Paid)
            return BadRequest(new { message = "This booking has already been paid. Refund it before cancelling." });

        var oldStatus = b.Status;
        var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
        b.Status = BookingStatus.Cancelled;
        b.CancellationReason = reason;
        b.CancelledByUserId = userId;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        AuditLogWriter.Add(db, AuditEntityType.Booking, b.BookingId, "Cancelled (staff)", userId,
            $"Status: {oldStatus} → Cancelled; Queue: {b.QueueNumber}; Reason: {reason}");
        await db.SaveChangesAsync(ct);

        await BroadcastAsync("QueueUpdated", b.DoctorId, new
        {
            booking_id = b.BookingId,
            doctor_id = b.DoctorId,
            status = b.Status.ToString()
        });
        return Ok(new { booking_id = b.BookingId, status = b.Status.ToString() });
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
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor && !actor.ActsAsDoctor(b.DoctorId)) return Forbid(); // a doctor only moves their own queue
        var illegal = BookingStatusMachine.RejectReason(b.Status, status);
        if (illegal is not null)
            return BadRequest(new { message = illegal });
        b.Status = status;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        //JJQR - AI (Nov 24, 2026) - owner: Admin setting "Mark the visit paid when the doctor completes it" (off by default; see Billing/AutoPaidOnComplete)
        Guid? autoPaid = null;
        if (status == BookingStatus.Completed)
        {
            var userId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (Guid?)null;
            autoPaid = await ClinicApp.Api.Billing.AutoPaidOnComplete.ApplyAsync(db, b.BookingId, userId, b.UpdatedAt, ct);
        }
        //JJQR - END
        await db.SaveChangesAsync(ct);

        await BroadcastAsync("QueueUpdated", b.DoctorId, new
        {
            booking_id = b.BookingId,
            doctor_id = b.DoctorId,
            status = b.Status.ToString()
        });
        //JJQR - AI (Nov 24, 2026) - the payments screens refresh like after "Record payment"
        if (autoPaid is { } paymentId)
            await BroadcastAsync("PaymentUpdated", b.DoctorId, new { booking_id = b.BookingId, payment_id = paymentId, status = PaymentStatus.Paid.ToString() });
        //JJQR - END

        return Ok(b);
    }

    /// <summary>Every real-time push goes to "staff" plus the owning doctor's
    /// own group — one call site instead of repeating both SendAsync calls
    /// at every mutation.</summary>
    private Task BroadcastAsync(string @event, Guid doctorId, object payload) =>
        Task.WhenAll(
            hub.Clients.Group("staff").SendAsync(@event, payload),
            hub.Clients.Group($"doctor:{doctorId}").SendAsync(@event, payload));
}
