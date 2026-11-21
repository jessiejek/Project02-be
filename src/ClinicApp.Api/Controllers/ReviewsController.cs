//JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
using ClinicApp.Api.Sync;
//JJQR - END
using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record CreateReviewRequest(Guid BookingId, Guid DoctorId, Guid PatientId, short Rating, string? Comment,
    //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
    Guid? ReviewId = null
    //JJQR - END
    );

/// <summary>reviews (contract §4). One per booking.</summary>
[ApiController]
[Authorize]
[Route("api/reviews")]
public class ReviewsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Review>>> GetAll(
        [FromQuery] Guid? doctorId, [FromQuery] Guid? bookingId, [FromQuery] Guid? patientId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.IsStaffLike && !actor.IsPatient) return Forbid();

        var q = db.Reviews.AsNoTracking().AsQueryable();
        if (actor.IsPatient)
        {
            // Doctor ratings are public to signed-in users (RLS reviews_select_authenticated), so a
            // patient may list a doctor's reviews. Anything else — by booking, by patient, or the
            // unfiltered list — is limited to their own reviews.
            if (patientId is not null && patientId != actor.PatientId) return Forbid();
            if (doctorId is null) q = q.Where(r => r.PatientId == actor.PatientId);
            else if (bookingId is not null) q = q.Where(r => r.PatientId == actor.PatientId);
        }
        if (doctorId is not null) q = q.Where(r => r.DoctorId == doctorId);
        if (bookingId is not null) q = q.Where(r => r.BookingId == bookingId);
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        return Ok(await q.OrderByDescending(r => r.CreatedAt).ToListAsync(ct));
    }

    /// <summary>A patient reviews a visit of their own. The patient and doctor are taken from
    /// the booking, never trusted from the body (RLS reviews_insert_own).</summary>
    [Authorize(Roles = "Patient")]
    [HttpPost]
    public async Task<ActionResult<Review>> Create(CreateReviewRequest req, CancellationToken ct)
    {
        if (req.Rating is < 1 or > 5) return BadRequest(new { message = "Rating must be 1–5." });

        var actor = await actors.ResolveAsync(User, ct);
        var booking = await db.Bookings.AsNoTracking()
            .SingleOrDefaultAsync(b => b.BookingId == req.BookingId && b.PatientId == actor.PatientId, ct);
        if (actor.PatientId is null || booking is null) return NotFound(); // not their booking (or no such booking)

        // JJQR - AI (Nov 20, 2026) - B-04: you can only review a visit that actually happened
        if (booking.Status != BookingStatus.Completed)
            return BadRequest(new { message = "You can review a visit only after it has been completed." });
        if (req.Comment is { Length: > 2000 })
            return BadRequest(new { message = "Comment is too long (max 2000 characters)." });
        // JJQR - END

        if (await db.Reviews.AnyAsync(r => r.BookingId == req.BookingId, ct))
            return Conflict(new { message = "This visit has already been reviewed." });

        //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
        if (await ClientIds.TakenAsync<Review>(db, req.ReviewId, ct)) return ClientIds.Conflict();
        //JJQR - END
        var row = new Review
        {
            //JJQR - AI (Nov 21, 2026) client-supplied id (offline sync)
            ReviewId = ClientIds.Use(req.ReviewId),
            //JJQR - END

            BookingId = booking.BookingId,
            DoctorId = booking.DoctorId,
            PatientId = booking.PatientId,
            Rating = req.Rating,
            Comment = req.Comment,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Reviews.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }
}
