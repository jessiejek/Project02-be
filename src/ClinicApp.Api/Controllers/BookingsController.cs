using ClinicApp.Api.Auditing;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    string? Notes);

public record UpdateBookingStatusRequest(BookingStatus Status, string? Reason);

public record CancelOwnBookingRequest(string? Reason);

public record CreatePatientBookingRequest(DateOnly? AppointmentDate, VisitType? VisitType, string? Notes);

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController(ClinicAppDbContext db) : ControllerBase
{
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
        return booking is null ? NotFound() : Ok(booking);
    }

    /// <summary>Admin/Staff only — this trusts `request.PatientId` from the body, which is
    /// safe only because callers are staff booking on a patient's behalf. Patients book
    /// themselves via `POST /api/bookings/book` instead, which locks the patient ID to the
    /// caller and doesn't accept an arbitrary doctor/service/slot payload.</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPost]
    public async Task<ActionResult<Booking>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.DoctorId == request.DoctorId, ct);
        if (doctor is null) return BadRequest(new { message = "Doctor not found." });

        var services = await db.Services.Where(s => request.ServiceIds.Contains(s.ServiceId)).ToListAsync(ct);
        if (services.Count != request.ServiceIds.Count) return BadRequest(new { message = "One or more services not found." });

        var now = DateTimeOffset.UtcNow;
        var totalFee = doctor.ConsultationFee + services.Sum(s => s.Price);

        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            PatientId = request.PatientId,
            DoctorId = request.DoctorId,
            AppointmentDate = request.AppointmentDate,
            SlotStartTime = request.SlotStartTime,
            SlotEndTime = request.SlotEndTime,
            Status = request.IsWalkIn ? BookingStatus.CheckedIn : BookingStatus.Pending,
            PaymentMode = request.PaymentMode,
            QueueNumber = await QueueSequencer.NextAsync(db, request.AppointmentDate, ct),
            ConsultationFeeSnapshot = doctor.ConsultationFee,
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
            PaymentId = Guid.NewGuid(),
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

        var created = await WithEmbeds().SingleAsync(b => b.BookingId == booking.BookingId, ct);
        return CreatedAtAction(nameof(GetById), new { id = booking.BookingId }, created);
    }

    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateBookingStatusRequest request, CancellationToken ct)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == id, ct);
        if (booking is null) return NotFound();

        var oldStatus = booking.Status;
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
        return Ok(new { booking_id = booking.BookingId, status = booking.Status.ToString() });
    }

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

        var doctor = await db.Doctors.OrderBy(d => d.DoctorId).FirstOrDefaultAsync(ct); // one doctor, same as QueueController
        if (doctor is null) return BadRequest(new { message = "No doctor configured." });

        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) return BadRequest(new { message = "Clinic settings missing." });

        var date = request.AppointmentDate ?? ClinicApp.Domain.ClinicClock.Today;
        if (date < ClinicApp.Domain.ClinicClock.Today)
        {
            return BadRequest(new { message = "Cannot book a past date." });
        }

        var visitType = request.VisitType ?? VisitType.New;
        var fee = ClinicApp.Domain.ClinicFees.Compute(settings, visitType, false, null);
        var now = DateTimeOffset.UtcNow;

        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            PatientId = patientId.Value,
            DoctorId = doctor.DoctorId,
            AppointmentDate = date,
            SlotStartTime = default,
            SlotEndTime = default,
            Status = BookingStatus.Pending,
            PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = await QueueSequencer.NextAsync(db, date, ct),
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
            PaymentId = Guid.NewGuid(),
            BookingId = booking.BookingId,
            Amount = fee.Total,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        });

        AuditLogWriter.Add(db, AuditEntityType.Booking, booking.BookingId, "Booked (online)",
            booking.CreatedByUserId, $"Date: {date:yyyy-MM-dd}; Queue: {booking.QueueNumber}; Fee: {fee.Total:0.00}");

        await db.SaveChangesAsync(ct);

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
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        var today = ClinicApp.Domain.ClinicClock.Today;
        var query = SearchBookings(WithEmbeds().Where(b => b.AppointmentDate == today), q).OrderBy(b => b.SlotStartTime);
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
