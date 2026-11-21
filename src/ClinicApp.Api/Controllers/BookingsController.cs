//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Auditing;
using ClinicApp.Api.Hubs;
using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record CreateBookingRequest(
    Guid PatientId,
    Guid DoctorId,
    DateOnly AppointmentDate,
    TimeOnly SlotStartTime,
    TimeOnly SlotEndTime,
    List<Guid> ServiceIds,
    PaymentMode PaymentMode,
    bool IsWalkIn,
    string? Notes,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? BookingId = null,
    Guid? PaymentId = null,
    //JJQR - END
    //JJQR - AI (21 Nov 2026) optional: visit_type (New | FollowUp) and discount_category (Senior | PWD) for desk-created bookings; absent = unchanged behaviour
    VisitType? VisitType = null,
    string? DiscountCategory = null
    //JJQR - END
    );

public record UpdateBookingStatusRequest(BookingStatus Status, string? Reason);

public record CancelOwnBookingRequest(string? Reason);

//JJQR - AI (Nov 21, 2026) client-supplied ids for the new booking made by a reschedule (offline sync)
public record RescheduleBookingRequest(DateOnly AppointmentDate, string? Reason, Guid? NewBookingId = null, Guid? NewPaymentId = null);
//JJQR - END

public record RescheduleBookingResponse(Guid OldBookingId, Guid NewBookingId, BookingStatus OldStatus, BookingStatus NewBookingStatus);

public record ClosedDateDto(DateOnly Date, string Reason);

public record CreatePatientBookingRequest(DateOnly? AppointmentDate, VisitType? VisitType, string? Notes,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? BookingId = null, Guid? PaymentId = null
    //JJQR - END
    );

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController(ClinicAppDbContext db, ActorResolver actors, IHubContext<ClinicHub> hub) : ControllerBase
{
    //JJQR - AI (Nov 21, 2026) - realtime: a status change made here (Undo check-in, no-show, cancel, reschedule, new booking) used to reach the
    // database but not SignalR, so the doctor's and the other staff screens stayed stale until their fallback poll. Same event + groups as QueueController.
    private Task NotifyQueueChangedAsync(Booking b) =>
        Task.WhenAll(
            hub.Clients.Group("staff").SendAsync("QueueUpdated", new { booking_id = b.BookingId, doctor_id = b.DoctorId, status = b.Status.ToString() }),
            hub.Clients.Group($"doctor:{b.DoctorId}").SendAsync("QueueUpdated", new { booking_id = b.BookingId, doctor_id = b.DoctorId, status = b.Status.ToString() }));
    //JJQR - END

    private IQueryable<Booking> WithEmbeds() =>
        db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .Include(b => b.Doctor).ThenInclude(d => d.StaffAccount)
            .Include(b => b.BookingServices).ThenInclude(bs => bs.Service)
            .Include(b => b.Payment);

    [HttpGet]
    public async Task<ActionResult<List<Booking>>> GetAll(
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? doctorId,
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] BookingStatus? status,
        CancellationToken ct)
    {
        // Patients only ever see their own bookings (RLS bookings_select_own_or_staff).
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();
        //JJQR - AI (Nov 19, 2026) - a Doctor only ever lists their OWN rows: their id replaces whatever ?doctorId= says (Staff / Admin unchanged)
        if (actor.IsDoctor)
        {
            if (actor.StaffId is not { } ownDoctor) return Forbid();
            doctorId = ownDoctor;
        }
        //JJQR - END

        var query = WithEmbeds();
        if (patientId is not null) query = query.Where(b => b.PatientId == patientId);
        if (doctorId is not null) query = query.Where(b => b.DoctorId == doctorId);
        if (date is not null) query = query.Where(b => b.AppointmentDate == date);
        if (from is not null) query = query.Where(b => b.AppointmentDate >= from);
        if (to is not null) query = query.Where(b => b.AppointmentDate <= to);
        if (status is not null) query = query.Where(b => b.Status == status);

        var bookings = await query.OrderByDescending(b => b.AppointmentDate).ThenBy(b => b.SlotStartTime).ToListAsync(ct);
        return Ok(bookings);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Booking>> GetById(Guid id, CancellationToken ct)
    {
        var booking = await WithEmbeds().SingleOrDefaultAsync(b => b.BookingId == id, ct);
        if (booking is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        return actor.CanAccessPatient(booking.PatientId) ? Ok(booking) : NotFound(); // 404, not 403: don't confirm the id exists
    }

    /// <summary>Admin/Staff only — this trusts `request.PatientId` from the body, which is
    /// safe only because callers are staff booking on a patient's behalf. Patients book
    /// themselves via `POST /api/bookings/book` instead, which locks the patient ID to the
    /// caller and doesn't accept an arbitrary doctor/service/slot payload.</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPost]
    public async Task<ActionResult<Booking>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (ClientIds.Supplied(request.BookingId))
        {
            var dup = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == request.BookingId, ct);
            if (dup is not null)
                return dup.PatientId == request.PatientId ? Ok(await WithEmbeds().SingleAsync(b => b.BookingId == dup.BookingId, ct)) : ClientIds.Conflict();
        }
        if (await ClientIds.TakenAsync<Payment>(db, request.PaymentId, ct)) return ClientIds.Conflict();
        //JJQR - END
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.DoctorId == request.DoctorId, ct);
        if (doctor is null) return BadRequest(new { message = "Doctor not found." });

        var services = await db.Services.Where(s => request.ServiceIds.Contains(s.ServiceId)).ToListAsync(ct);
        if (services.Count != request.ServiceIds.Count) return BadRequest(new { message = "One or more services not found." });

        // JJQR - AI (Nov 20, 2026) - B-06 / B-29 / B-05: a scheduled booking follows the same date rules as online
        // booking; a walk-in is someone standing at the desk, so the clinic is by definition open for them.
        if (!request.IsWalkIn)
        {
            var closedReason = await ClosedReasonAsync(request.AppointmentDate, doctor.DoctorId, ct);
            if (closedReason is not null) return BadRequest(new { message = closedReason });
            if (await HasActiveBookingOnAsync(request.PatientId, request.AppointmentDate, null, ct))
                return Conflict(new { message = "This patient already has a booking for that date." });
        }
        // JJQR - END

        var now = DateTimeOffset.UtcNow;
        var totalFee = doctor.ConsultationFee + services.Sum(s => s.Price);

        //JJQR - AI (21 Nov 2026) optional visit type / discount. Only when sent: the consultation part of the fee comes from the clinic fee schedule (same ClinicFees.Compute as walk-in check-in) and the
        // selected services are added on top; the discount must be backed by the patient (Senior: 60+ or a Senior ID on file; PWD: a PWD ID on file). Nothing sent = exactly the old fee and behaviour.
        var visitType = request.VisitType ?? VisitType.New;
        string? discount = null;
        decimal discountAmount = 0m;
        var consultationSnapshot = doctor.ConsultationFee;
        if (request.VisitType is not null || !string.IsNullOrWhiteSpace(request.DiscountCategory))
        {
            if (!QueueLane.IsValidDiscount(request.DiscountCategory))
                return BadRequest(new { message = "Discount must be Senior or PWD." });
            discount = QueueLane.NormalizeDiscount(request.DiscountCategory);
            var patientForFee = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == request.PatientId, ct);
            if (patientForFee is null) return BadRequest(new { message = "Patient not found." });
            if (discount == "Senior" && string.IsNullOrWhiteSpace(patientForFee.SeniorIdNumber) && !QueueLane.IsSenior(patientForFee.DateOfBirth, request.AppointmentDate))
                return BadRequest(new { message = "A Senior discount needs a patient aged 60 or over, or a Senior ID number on file." });
            if (discount == "PWD" && string.IsNullOrWhiteSpace(patientForFee.PwdIdNumber))
                return BadRequest(new { message = "A PWD discount needs a PWD ID number on file." });
            var clinic = await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(c => c.Id == 1, ct);
            if (clinic is null) return BadRequest(new { message = "Clinic settings missing." });
            var fee = ClinicFees.Compute(clinic, visitType, false, discount);
            consultationSnapshot = fee.Subtotal;
            discountAmount = fee.DiscountAmount;
            totalFee = fee.Total + services.Sum(s => s.Price);
        }
        //JJQR - END

        var booking = new Booking
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            BookingId = ClientIds.Use(request.BookingId),
            //JJQR - END
            PatientId = request.PatientId,
            DoctorId = request.DoctorId,
            AppointmentDate = request.AppointmentDate,
            SlotStartTime = request.SlotStartTime,
            SlotEndTime = request.SlotEndTime,
            Status = request.IsWalkIn ? BookingStatus.CheckedIn : BookingStatus.Pending,
            PaymentMode = request.PaymentMode,
            QueueNumber = await QueueSequencer.NextForPatientAsync(db, request.AppointmentDate, request.PatientId, discount, ct), //JJQR - AI (Nov 21, 2026) - priority lane // JJQR - AI (21 Nov 2026) discount picks the S- lane // JJQR - END
            ConsultationFeeSnapshot = consultationSnapshot, //JJQR - AI (21 Nov 2026) // JJQR - END
            VisitType = visitType, DiscountCategory = discount, DiscountAmount = discountAmount, //JJQR - AI (21 Nov 2026) // JJQR - END
            TotalFee = totalFee,
            AmountDue = totalFee,
            IsWalkIn = request.IsWalkIn,
            Notes = request.Notes,
            CreatedByUserId = CurrentUserId(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Bookings.Add(booking);
        foreach (var service in services)
        {
            db.BookingServices.Add(new BookingService { BookingId = booking.BookingId, ServiceId = service.ServiceId, PriceAtBooking = service.Price });
        }

        db.Payments.Add(new Payment
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            PaymentId = ClientIds.Use(request.PaymentId),
            //JJQR - END
            BookingId = booking.BookingId,
            Amount = totalFee,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        });

        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId,
            request.IsWalkIn ? "Booked (walk-in)" : "Booked",
            booking.CreatedByUserId,
            $"Doctor: {doctor.DoctorId}; Date: {booking.AppointmentDate:yyyy-MM-dd} {booking.SlotStartTime}; Fee: {totalFee:0.00}");

        await db.SaveChangesAsync(ct);
        await NotifyQueueChangedAsync(booking); //JJQR - AI (Nov 21, 2026) - realtime

        var created = await WithEmbeds().SingleAsync(b => b.BookingId == booking.BookingId, ct);
        return CreatedAtAction(nameof(GetById), new { id = booking.BookingId }, created);
    }

    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateBookingStatusRequest request, CancellationToken ct)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == id, ct);
        if (booking is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor && !actor.ActsAsDoctor(booking.DoctorId)) return Forbid(); // a doctor only moves their own bookings

        var oldStatus = booking.Status;
        var illegal = BookingStatusMachine.RejectReason(oldStatus, request.Status);
        if (illegal is not null)
            return BadRequest(new { message = illegal });

        //JJQR - AI (Nov 21, 2026) - cancelling a paid booking would orphan the money; the queue cancel already refuses it, so this endpoint does too (refund first)
        if (request.Status == BookingStatus.Cancelled
            && await db.Payments.AsNoTracking().AnyAsync(p => p.BookingId == id && p.Status == PaymentStatus.Paid, ct))
            return Conflict(new { message = "This booking has already been paid. Refund it before cancelling." });
        //JJQR - END

        booking.Status = request.Status;
        if (request.Status == BookingStatus.Cancelled)
        {
            booking.CancellationReason = request.Reason;
            booking.CancelledByUserId = CurrentUserId();
        }
        booking.UpdatedAt = DateTimeOffset.UtcNow;

        var userId = CurrentUserId();
        var details = request.Status == BookingStatus.Cancelled && !string.IsNullOrWhiteSpace(request.Reason)
            ? $"Status: {oldStatus} → {request.Status}; Reason: {request.Reason}"
            : $"Status: {oldStatus} → {request.Status}";
        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId, request.Status.ToString(), userId, details);

        await db.SaveChangesAsync(ct);
        await NotifyQueueChangedAsync(booking); //JJQR - AI (Nov 21, 2026) - realtime
        return Ok(booking);
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("walk-in")]
    public Task<ActionResult<Booking>> CreateWalkIn(CreateBookingRequest request, CancellationToken ct) =>
        Create(request with { IsWalkIn = true }, ct);

    // ── Role-scoped views (confirmed against actual FE calls) ──────────────

    /// <summary>The logged-in patient's own bookings, paged.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<PagedResult<Booking>>> GetMine([FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        var patientId = await CurrentPatientIdAsync(ct);
        if (patientId is null) return Forbid();

        var query = WithEmbeds().Where(b => b.PatientId == patientId).OrderByDescending(b => b.AppointmentDate);
        return Ok(await PageAsync(query, page, pageSize, ct));
    }

    /// <summary>A patient cancels their own booking before arriving. Only `Pending` (booked,
    /// not yet checked in) can be cancelled here — once staff have checked them in it is the
    /// clinic's queue, so changes go through staff. The row is kept as `Cancelled` (not
    /// deleted) so history/audit stay intact, and `GetQueue` already hides cancelled rows.</summary>
    [Authorize(Roles = "Patient")]
    [HttpPut("{id:guid}/cancel")]
    public async Task<IActionResult> CancelOwn(Guid id, CancelOwnBookingRequest? request, CancellationToken ct)
    {
        var patientId = await CurrentPatientIdAsync(ct);
        if (patientId is null) return Forbid();

        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == id && b.PatientId == patientId, ct);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Pending)
            return BadRequest(new { message = "Only a booking that hasn't been checked in yet can be cancelled." });

        var userId = CurrentUserId();
        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = string.IsNullOrWhiteSpace(request?.Reason) ? "Cancelled by patient" : request!.Reason.Trim();
        booking.CancelledByUserId = userId;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId, "Cancelled (patient)", userId,
            $"Queue: {booking.QueueNumber}; Date: {booking.AppointmentDate:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
        await NotifyQueueChangedAsync(booking); //JJQR - AI (Nov 21, 2026) - realtime
        return Ok(new { booking_id = booking.BookingId, status = booking.Status.ToString() });
    }

    //JJQR - AI (Oct 31, 2026)
    /// <summary>Staff/admin reschedule: mark the current row <c>Rescheduled</c> and clone a new
    /// booking on the requested date with a fresh queue number (audit-friendly).</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPut("{id:guid}/reschedule")]
    public async Task<ActionResult<RescheduleBookingResponse>> Reschedule(Guid id, RescheduleBookingRequest request, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.BookingServices)
            .SingleOrDefaultAsync(b => b.BookingId == id, ct);
        if (booking is null) return NotFound();

        //JJQR - AI (Nov 21, 2026) client-supplied ids: the same reschedule sent again (same new_booking_id, same patient and date) returns the earlier result; any other reuse of an id is a 409
        if (ClientIds.Supplied(request.NewBookingId))
        {
            var dup = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == request.NewBookingId, ct);
            if (dup is not null)
                return dup.PatientId == booking.PatientId && booking.Status == BookingStatus.Rescheduled && dup.AppointmentDate == request.AppointmentDate
                    ? Ok(new RescheduleBookingResponse(booking.BookingId, dup.BookingId, BookingStatus.Rescheduled, dup.Status))
                    : ClientIds.Conflict();
        }
        if (await ClientIds.TakenAsync<Payment>(db, request.NewPaymentId, ct)) return ClientIds.Conflict();
        //JJQR - END

        var oldStatus = booking.Status;
        var illegal = BookingStatusMachine.RejectReason(oldStatus, BookingStatus.Rescheduled);
        if (illegal is not null)
            return BadRequest(new { message = illegal });

        if (request.AppointmentDate < ClinicApp.Domain.ClinicClock.Today)
            return BadRequest(new { message = "Cannot reschedule to a past date." });

        // JJQR - AI (Nov 20, 2026) - B-06 / B-29 / B-05: same date rules as a new booking
        var closedOn = await ClosedReasonAsync(request.AppointmentDate, booking.DoctorId, ct);
        if (closedOn is not null) return BadRequest(new { message = closedOn });
        if (await HasActiveBookingOnAsync(booking.PatientId, request.AppointmentDate, booking.BookingId, ct))
            return Conflict(new { message = "This patient already has a booking for that date." });
        // JJQR - END

        var userId = CurrentUserId();
        var now = DateTimeOffset.UtcNow;
        booking.Status = BookingStatus.Rescheduled;
        booking.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            var note = $"Rescheduled to {request.AppointmentDate:yyyy-MM-dd}: {request.Reason.Trim()}";
            booking.Notes = string.IsNullOrWhiteSpace(booking.Notes) ? note : $"{booking.Notes}; {note}";
        }

        var newStatus = oldStatus switch
        {
            BookingStatus.Pending => BookingStatus.Pending,
            BookingStatus.ProofSubmitted => BookingStatus.ProofSubmitted,
            _ => BookingStatus.Confirmed,
        };

        var newId = ClientIds.Use(request.NewBookingId); //JJQR - AI (Nov 21, 2026) client-supplied id // JJQR - END
        var newBooking = new Booking
        {
            BookingId = newId,
            PatientId = booking.PatientId,
            DoctorId = booking.DoctorId,
            AppointmentDate = request.AppointmentDate,
            SlotStartTime = booking.SlotStartTime,
            SlotEndTime = booking.SlotEndTime,
            Status = newStatus,
            PaymentMode = booking.PaymentMode,
            QueueNumber = await QueueSequencer.NextForPatientAsync(db, request.AppointmentDate, booking.PatientId, booking.DiscountCategory, ct), //JJQR - AI (Nov 21, 2026) - priority lane
            ConsultationFeeSnapshot = booking.ConsultationFeeSnapshot,
            TotalFee = booking.TotalFee,
            AmountDue = booking.AmountDue,
            IsWalkIn = booking.IsWalkIn,
            VisitType = booking.VisitType,
            DiscountCategory = booking.DiscountCategory,
            DiscountAmount = booking.DiscountAmount,
            MedCertRequested = booking.MedCertRequested,
            Notes = null,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Bookings.Add(newBooking);
        foreach (var line in booking.BookingServices)
        {
            db.BookingServices.Add(new BookingService
            {
                BookingId = newId,
                ServiceId = line.ServiceId,
                PriceAtBooking = line.PriceAtBooking,
            });
        }

        db.Payments.Add(new Payment
        {
            PaymentId = ClientIds.Use(request.NewPaymentId), //JJQR - AI (Nov 21, 2026) client-supplied id // JJQR - END
            BookingId = newId,
            Amount = booking.TotalFee,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now,
        });

        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId, "Rescheduled", userId,
            $"→ {request.AppointmentDate:yyyy-MM-dd}; new booking {newId}");
        AuditLogWriter.Add(db, AuditEntityType.Booking, newId, "Booked (reschedule)", userId,
            $"From {booking.BookingId}; Queue: {newBooking.QueueNumber}");

        await db.SaveChangesAsync(ct);
        await NotifyQueueChangedAsync(booking); //JJQR - AI (Nov 21, 2026) - realtime
        await NotifyQueueChangedAsync(newBooking);
        return Ok(new RescheduleBookingResponse(booking.BookingId, newId, BookingStatus.Rescheduled, newStatus));
    }
    //JJQR - END

    // JJQR - AI (Nov 20, 2026) - B-06 / B-29 / B-05: shared "can this date be booked?" rules
    private static readonly BookingStatus[] ActiveStatuses =
        [BookingStatus.Pending, BookingStatus.ProofSubmitted, BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.InProgress, BookingStatus.OnHold];

    /// <summary>Why a scheduled (non-walk-in) booking cannot be made for this day, or null when it can:
    /// the clinic's operating hours mark that weekday closed (B-29), the doctor blocked the date, or the
    /// doctor's weekly schedule has that weekday off (B-06). A doctor with no working day at all is
    /// treated as "no schedule configured" and is not restricted by weekday.</summary>
    private async Task<string?> ClosedReasonAsync(DateOnly date, Guid doctorId, CancellationToken ct)
    {
        var dow = (short)date.DayOfWeek; // 0 = Sunday, same numbering as clinic_operating_hours / doctor_schedules
        var hours = await db.ClinicOperatingHours.AsNoTracking().SingleOrDefaultAsync(h => h.DayOfWeek == dow, ct);
        if (hours is { IsClosed: true })
            return $"The clinic is closed on {date.DayOfWeek}s. Please choose another day.";

        if (await db.DoctorBlockedDates.AsNoTracking().AnyAsync(b => b.DoctorId == doctorId && b.BlockedDate == date, ct))
            return "The doctor is not available on that date. Please choose another day.";

        // The weekly schedule only restricts booking once someone has actually set one up (at least one working
        // day). A doctor whose 7 rows are all switched off — the default state — means "no schedule configured".
        var schedule = await db.DoctorSchedules.AsNoTracking().Where(x => x.DoctorId == doctorId).ToListAsync(ct);
        if (schedule.Any(x => x.IsActive))
        {
            var day = schedule.FirstOrDefault(x => x.DayOfWeek == dow);
            if (day is null || !day.IsActive)
                return $"The doctor does not see patients on {date.DayOfWeek}s. Please choose another day.";
        }
        return null;
    }

    private Task<bool> HasActiveBookingOnAsync(Guid patientId, DateOnly date, Guid? exceptBookingId, CancellationToken ct) =>
        db.Bookings.AsNoTracking().AnyAsync(b => b.PatientId == patientId && b.AppointmentDate == date
            && b.BookingId != exceptBookingId && ActiveStatuses.Contains(b.Status), ct);
    // JJQR - END

    // JJQR - AI (Nov 20, 2026) - B-06 / B-29 (UI): which upcoming days can't be booked online, so the date picker can say so up front
    /// <summary>Dates in the next <paramref name="days"/> days (default 90, max 366) that online booking refuses,
    /// with the reason shown to the patient. Same rules as <c>POST /api/bookings/book</c>.</summary>
    [Authorize(Roles = "Patient,Staff,Admin")]
    [HttpGet("closed-dates")]
    public async Task<ActionResult<List<ClosedDateDto>>> ClosedDates([FromQuery] int days = 90, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 366);
        var doctor = await db.Doctors.AsNoTracking().OrderBy(d => d.DoctorId).FirstOrDefaultAsync(ct);
        if (doctor is null) return Ok(new List<ClosedDateDto>());
        var result = new List<ClosedDateDto>();
        var start = ClinicApp.Domain.ClinicClock.Today;
        for (var i = 0; i < days; i++)
        {
            var date = start.AddDays(i);
            var reason = await ClosedReasonAsync(date, doctor.DoctorId, ct);
            if (reason is not null) result.Add(new ClosedDateDto(date, reason));
        }
        return Ok(result);
    }
    // JJQR - END

    /// <summary>Online self-booking (§16.3 hybrid queue): a patient joins the same per-day
    /// FCFS queue walk-ins use, without visiting the clinic first. No slot picker, no
    /// capacity cap — booking just reserves a `queue_number` in creation order, same as a
    /// walk-in reserves one at check-in; whichever happens first gets the earlier number.
    /// The patient still has to physically check in on arrival (Pending → CheckedIn), same
    /// as today's flow — this only replaces "get your number from the front desk" with
    /// "get it from your phone."</summary>
    [Authorize(Roles = "Patient")]
    [HttpPost("book")]
    public async Task<ActionResult<Booking>> BookOnline(CreatePatientBookingRequest request, CancellationToken ct)
    {
        var patientId = await CurrentPatientIdAsync(ct);
        if (patientId is null) return Forbid();

        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (ClientIds.Supplied(request.BookingId))
        {
            var dup = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == request.BookingId, ct);
            if (dup is not null)
                return dup.PatientId == patientId ? Ok(await WithEmbeds().SingleAsync(b => b.BookingId == dup.BookingId, ct)) : ClientIds.Conflict();
        }
        if (await ClientIds.TakenAsync<Payment>(db, request.PaymentId, ct)) return ClientIds.Conflict();
        //JJQR - END
        var doctor = await db.Doctors.OrderBy(d => d.DoctorId).FirstOrDefaultAsync(ct); // one doctor, same as QueueController
        if (doctor is null) return BadRequest(new { message = "No doctor configured." });

        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) return BadRequest(new { message = "Clinic settings missing." });

        var date = request.AppointmentDate ?? ClinicApp.Domain.ClinicClock.Today;
        if (date < ClinicApp.Domain.ClinicClock.Today)
        {
            return BadRequest(new { message = "Cannot book a past date." });
        }

        // JJQR - AI (Nov 20, 2026) - B-06 / B-29 / B-05
        var closedReason = await ClosedReasonAsync(date, doctor.DoctorId, ct);
        if (closedReason is not null) return BadRequest(new { message = closedReason });
        if (await HasActiveBookingOnAsync(patientId.Value, date, null, ct))
            return Conflict(new { message = "You already have a booking for that date." });
        // JJQR - END

        var visitType = request.VisitType ?? VisitType.New;
        var fee = ClinicApp.Domain.ClinicFees.Compute(settings, visitType, false, null);
        var now = DateTimeOffset.UtcNow;

        var booking = new Booking
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            BookingId = ClientIds.Use(request.BookingId),
            //JJQR - END
            PatientId = patientId.Value,
            DoctorId = doctor.DoctorId,
            AppointmentDate = date,
            SlotStartTime = default,
            SlotEndTime = default,
            Status = BookingStatus.Pending,
            PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = await QueueSequencer.NextForPatientAsync(db, date, patientId.Value, null, ct), //JJQR - AI (Nov 21, 2026) - priority lane
            VisitType = visitType,
            ConsultationFeeSnapshot = fee.Subtotal,
            TotalFee = fee.Total,
            AmountDue = fee.Total,
            IsWalkIn = false,
            Notes = request.Notes,
            CreatedByUserId = CurrentUserId(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Bookings.Add(booking);
        db.Payments.Add(new Payment
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            PaymentId = ClientIds.Use(request.PaymentId),
            //JJQR - END
            BookingId = booking.BookingId,
            Amount = fee.Total,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        });

        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId, "Booked (online)",
            booking.CreatedByUserId, $"Date: {date:yyyy-MM-dd}; Queue: {booking.QueueNumber}; Fee: {fee.Total:0.00}");

        await db.SaveChangesAsync(ct);
        await NotifyQueueChangedAsync(booking); //JJQR - AI (Nov 21, 2026) - realtime

        var created = await WithEmbeds().SingleAsync(b => b.BookingId == booking.BookingId, ct);
        return CreatedAtAction(nameof(GetById), new { id = booking.BookingId }, created);
    }

    /// <summary>The logged-in doctor's bookings for today.</summary>
    [Authorize(Roles = "Doctor")]
    [HttpGet("doctor/today")]
    public async Task<ActionResult<List<Booking>>> GetDoctorToday(CancellationToken ct)
    {
        var doctorId = await CurrentDoctorIdAsync(ct);
        if (doctorId is null) return Forbid();

        var today = ClinicApp.Domain.ClinicClock.Today;
        var bookings = await WithEmbeds()
            .Where(b => b.DoctorId == doctorId && b.AppointmentDate == today)
            .OrderBy(b => b.SlotStartTime)
            .ToListAsync(ct);

        return Ok(bookings);
    }

    /// <summary>Counts for the doctor dashboard queue widget — camelCase, computed (not a table).</summary>
    [Authorize(Roles = "Doctor")]
    [HttpGet("doctor/today-summary")]
    public async Task<ActionResult<object>> GetDoctorTodaySummary(CancellationToken ct)
    {
        var doctorId = await CurrentDoctorIdAsync(ct);
        if (doctorId is null) return Forbid();

        var today = ClinicApp.Domain.ClinicClock.Today;
        var todays = await db.Bookings.AsNoTracking().Where(b => b.DoctorId == doctorId && b.AppointmentDate == today).ToListAsync(ct);

        return Ok(new
        {
            bookedToday = todays.Count,
            checkedIn = todays.Count(b => b.Status == BookingStatus.CheckedIn),
            waiting = todays.Count(b => b.Status is BookingStatus.CheckedIn or BookingStatus.OnHold),
            completed = todays.Count(b => b.Status == BookingStatus.Completed),
            noShow = todays.Count(b => b.Status == BookingStatus.NoShow)
        });
    }

    /// <summary>Distinct patients the logged-in doctor has ever had a booking with.</summary>
    [Authorize(Roles = "Doctor")]
    [HttpGet("doctor/patients")]
    public async Task<ActionResult<List<Booking>>> GetDoctorPatients(CancellationToken ct)
    {
        var doctorId = await CurrentDoctorIdAsync(ct);
        if (doctorId is null) return Forbid();

        var bookings = await WithEmbeds()
            .Where(b => b.DoctorId == doctorId)
            .OrderByDescending(b => b.AppointmentDate)
            .ToListAsync(ct);

        return Ok(bookings);
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("staff/today")]
    public async Task<ActionResult<PagedResult<Booking>>> GetStaffToday(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null,
        // JJQR - AI (Nov 20, 2026) - B-42: the front-desk dashboard asks for live rows only (no Cancelled / Expired), so they are neither listed nor counted
        [FromQuery] bool activeOnly = false,
        // Optional comma-separated BookingStatus list (e.g. "Confirmed,CheckedIn"): the dashboard's arrivals list only needs those, so a busy day cannot push them past the page cap.
        [FromQuery] string? statuses = null,
        // JJQR - END
        CancellationToken ct = default)
    {
        var today = ClinicApp.Domain.ClinicClock.Today;
        var todays = WithEmbeds().Where(b => b.AppointmentDate == today);
        if (activeOnly) todays = todays.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired);
        if (!string.IsNullOrWhiteSpace(statuses))
        {
            var wanted = statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => Enum.TryParse<BookingStatus>(x, true, out var st) ? (BookingStatus?)st : null)
                .Where(x => x is not null).Select(x => x!.Value).ToList();
            todays = todays.Where(b => wanted.Contains(b.Status));
        }
        var query = SearchBookings(todays, q).OrderBy(b => b.SlotStartTime);
        return Ok(await PageAsync(query, page, pageSize, ct));
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("staff/all")]
    public async Task<ActionResult<PagedResult<Booking>>> GetStaffAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        var query = SearchBookings(WithEmbeds(), q).OrderByDescending(b => b.AppointmentDate);
        return Ok(await PageAsync(query, page, pageSize, ct));
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("staff/for-payment")]
    public async Task<ActionResult<PagedResult<Booking>>> GetStaffForPayment(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        var query = SearchBookings(
                WithEmbeds().Where(b => b.Payment != null && b.Payment.Status == PaymentStatus.Unpaid), q)
            .OrderBy(b => b.AppointmentDate);
        return Ok(await PageAsync(query, page, pageSize, ct));
    }

    /// <summary>§16.2 — `q` matches patient name / code / queue number.</summary>
    private static IQueryable<Booking> SearchBookings(IQueryable<Booking> query, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return query;
        var s = q.Trim();
        return query.Where(b =>
            (b.Patient.FirstName + " " + b.Patient.LastName).Contains(s) ||
            b.Patient.PatientCode.Contains(s) ||
            (b.QueueNumber != null && b.QueueNumber.Contains(s)));
    }

    private async Task<PagedResult<Booking>> PageAsync(IOrderedQueryable<Booking> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<Booking> { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
    }

    private async Task<Guid?> CurrentPatientIdAsync(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;
        return await db.Patients.Where(p => p.UserId == userId).Select(p => (Guid?)p.PatientId).SingleOrDefaultAsync(ct);
    }

    private async Task<Guid?> CurrentDoctorIdAsync(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;
        return await db.StaffAccounts.Where(s => s.UserId == userId).Select(s => (Guid?)s.StaffId).SingleOrDefaultAsync(ct);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
