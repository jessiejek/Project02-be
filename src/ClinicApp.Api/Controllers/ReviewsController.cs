using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record CreateReviewRequest(Guid BookingId, Guid DoctorId, Guid PatientId, short Rating, string? Comment);

/// <summary>reviews (contract §4). One per booking.</summary>
[ApiController]
[Authorize]
[Route("api/reviews")]
public class ReviewsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Review>>> GetAll(
        [FromQuery] Guid? doctorId, [FromQuery] Guid? bookingId, [FromQuery] Guid? patientId, CancellationToken ct)
    {
        var q = db.Reviews.AsNoTracking().AsQueryable();
        if (doctorId is not null) q = q.Where(r => r.DoctorId == doctorId);
        if (bookingId is not null) q = q.Where(r => r.BookingId == bookingId);
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        return Ok(await q.OrderByDescending(r => r.CreatedAt).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<Review>> Create(CreateReviewRequest req, CancellationToken ct)
    {
        if (req.Rating is < 1 or > 5) return BadRequest(new { message = "Rating must be 1–5." });
        if (await db.Reviews.AnyAsync(r => r.BookingId == req.BookingId, ct))
            return Conflict(new { message = "This visit has already been reviewed." });

        var row = new Review
        {
            ReviewId = Guid.NewGuid(),
            BookingId = req.BookingId,
            DoctorId = req.DoctorId,
            PatientId = req.PatientId,
            Rating = req.Rating,
            Comment = req.Comment,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Reviews.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }
}
