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
        [FromQuery] BookingStatus? status,
        CancellationToken ct)
    {
        var query = WithEmbeds();
        if (patientId is not null) query = query.Where(b => b.PatientId == patientId);
        if (doctorId is not null) query = query.Where(b => b.DoctorId == doctorId);
        if (date is not null) query = query.Where(b => b.AppointmentDate == date);
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
            QueueNumber = await NextQueueNumberAsync(request.DoctorId, request.AppointmentDate, ct),
            ConsultationFeeSnapshot = doctor.ConsultationFee,
            TotalFee = totalFee,
            AmountDue = totalFee,
            IsWalkIn = request.IsWalkIn,
            Notes = request.Notes,
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

        booking.Status = request.Status;
        if (request.Status == BookingStatus.Cancelled)
        {
            booking.CancellationReason = request.Reason;
            booking.CancelledByUserId = CurrentUserId();
        }

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

    /// <summary>The logged-in doctor's bookings for today.</summary>
    [Authorize(Roles = "Doctor")]
    [HttpGet("doctor/today")]
    public async Task<ActionResult<List<Booking>>> GetDoctorToday(CancellationToken ct)
    {
        var doctorId = await CurrentDoctorIdAsync(ct);
        if (doctorId is null) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
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
    public async Task<ActionResult<PagedResult<Booking>>> GetStaffToday([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var query = WithEmbeds().Where(b => b.AppointmentDate == today).OrderBy(b => b.SlotStartTime);
        return Ok(await PageAsync(query, page, pageSize, ct));
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("staff/all")]
    public async Task<ActionResult<PagedResult<Booking>>> GetStaffAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = WithEmbeds().OrderByDescending(b => b.AppointmentDate);
        return Ok(await PageAsync(query, page, pageSize, ct));
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("staff/for-payment")]
    public async Task<ActionResult<PagedResult<Booking>>> GetStaffForPayment([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = WithEmbeds()
            .Where(b => b.Payment != null && b.Payment.Status == PaymentStatus.Unpaid)
            .OrderBy(b => b.AppointmentDate);
        return Ok(await PageAsync(query, page, pageSize, ct));
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

    private async Task<string> NextQueueNumberAsync(Guid doctorId, DateOnly date, CancellationToken ct)
    {
        // Count-based queue number per plan §8 (BookingsController: "queue number (count-based)").
        var countToday = await db.Bookings.CountAsync(b => b.DoctorId == doctorId && b.AppointmentDate == date, ct);
        return $"Q-{countToday + 1:D3}";
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
