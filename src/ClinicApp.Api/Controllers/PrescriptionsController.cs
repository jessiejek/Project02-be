using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record RxItemInput(
    Guid MedicineId, string GenericName, string Dosage, string Quantity,
    string? Instruction, bool IsControlledSubstance,
    // §16.8 Form 1 — structured Rx-pad fields, all optional.
    string? Timing = null, string? MealRelation = null,
    string? DurationKind = null, int? DurationValue = null, string? Indication = null);
public record UpsertRxGroupRequest(Guid PatientId, Guid DoctorId, Guid BookingId, List<RxItemInput> Items);
public record UpsertRxTemplateRequest(Guid DoctorId, string Title, bool IsSystemTemplate, List<RxItemInput> Items);
public record FavoriteMedicineInput(Guid MedicineId, string GenericName, string Dosage, string Quantity, string? Instruction);

/// <summary>prescription_groups / _line_items / _templates / _template_items /
/// doctor_favorite_medicines (contract §4/§6).</summary>
[ApiController]
[Authorize]
[Route("api")]
public class PrescriptionsController(ClinicAppDbContext db, ActorResolver actors) : ControllerBase
{
    // ── prescription_groups (+ line items) ──────────────────────────────────
    // §6 embed: prescription_groups(bookings(appointment_date, doctors(staff_accounts(full_name)))).
    private IQueryable<PrescriptionGroup> Groups() =>
        db.PrescriptionGroups.AsNoTracking()
            .Include(g => g.LineItems)
            .Include(g => g.Booking).ThenInclude(b => b!.Doctor).ThenInclude(d => d!.StaffAccount);

    [HttpGet("prescription-groups")]
    public async Task<ActionResult<List<PrescriptionGroup>>> GetGroups(
        [FromQuery] Guid? patientId, [FromQuery] Guid? bookingId, [FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsPatient)
        {
            if (actor.PatientId is null || (patientId is not null && patientId != actor.PatientId)) return Forbid();
            patientId = actor.PatientId;
        }
        else if (!actor.IsStaffLike) return Forbid();

        var q = Groups();
        if (patientId is not null) q = q.Where(g => g.PatientId == patientId);
        if (bookingId is not null) q = q.Where(g => g.BookingId == bookingId);
        if (doctorId is not null) q = q.Where(g => g.DoctorId == doctorId);
        return Ok(await q.OrderByDescending(g => g.CreatedAt).ToListAsync(ct));
    }

    [HttpGet("prescription-groups/{id:guid}")]
    public async Task<ActionResult<PrescriptionGroup>> GetGroup(Guid id, CancellationToken ct)
    {
        var g = await Groups().SingleOrDefaultAsync(x => x.GroupId == id, ct);
        if (g is null) return NotFound();
        return (await actors.ResolveAsync(User, ct)).CanAccessPatient(g.PatientId) ? Ok(g) : NotFound();
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpDelete("prescription-groups/{id:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid id, CancellationToken ct)
    {
        var g = await db.PrescriptionGroups.SingleOrDefaultAsync(x => x.GroupId == id, ct);
        if (g is null) return NotFound();
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(g.DoctorId)) return Forbid();
        db.PrescriptionGroups.Remove(g); // line items cascade
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Upsert the (one) Rx group for a booking + replace its line items.</summary>
    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("prescription-groups/by-booking/{bookingId:guid}")]
    public async Task<ActionResult<PrescriptionGroup>> UpsertGroupByBooking(Guid bookingId, UpsertRxGroupRequest req, CancellationToken ct)
    {
        // Patient / doctor come from the booking, not the body; a doctor only writes their own visits.
        var ownerBooking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (ownerBooking is null) return NotFound(new { message = "Booking not found." });
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(ownerBooking.DoctorId)) return Forbid();
        if (req.PatientId != ownerBooking.PatientId || req.DoctorId != ownerBooking.DoctorId)
            return BadRequest(new { message = "Patient / doctor do not match the booking." });

        var now = DateTimeOffset.UtcNow;
        var g = await db.PrescriptionGroups.Include(x => x.LineItems).SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (g is null)
        {
            g = new PrescriptionGroup { GroupId = Guid.NewGuid(), BookingId = bookingId, CreatedAt = now };
            db.PrescriptionGroups.Add(g);
        }
        g.PatientId = req.PatientId;
        g.DoctorId = req.DoctorId;
        g.UpdatedAt = now;

        db.PrescriptionLineItems.RemoveRange(g.LineItems);
        foreach (var i in req.Items)
        {
            db.PrescriptionLineItems.Add(new PrescriptionLineItem
            {
                Id = Guid.NewGuid(), GroupId = g.GroupId, MedicineId = i.MedicineId,
                GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
                Instruction = i.Instruction, IsControlledSubstance = i.IsControlledSubstance,
                Timing = i.Timing, MealRelation = i.MealRelation,
                DurationKind = i.DurationKind, DurationValue = i.DurationValue, Indication = i.Indication,
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
        return Ok(await db.PrescriptionGroups.AsNoTracking().Include(x => x.LineItems).SingleAsync(x => x.GroupId == g.GroupId, ct));
    }

    // ── prescription_templates (+ items) ───────────────────────────────────
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpGet("prescription-templates")]
    public async Task<ActionResult<List<PrescriptionTemplate>>> GetTemplates([FromQuery] Guid? doctorId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsDoctor)
        {
            // A doctor sees only their own sets + system sets, never another doctor's.
            if (doctorId is not null && !actor.ActsAsDoctor(doctorId.Value)) return Forbid();
            doctorId = actor.StaffId;
        }

        var q = db.PrescriptionTemplates.AsNoTracking().Include(t => t.Items).AsQueryable();
        if (doctorId is not null) q = q.Where(t => t.DoctorId == doctorId || t.IsSystemTemplate);
        return Ok(await q.OrderBy(t => t.Title).ToListAsync(ct));
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpPost("prescription-templates")]
    public async Task<ActionResult<PrescriptionTemplate>> CreateTemplate(UpsertRxTemplateRequest req, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(req.DoctorId)) return Forbid();
        if (req.IsSystemTemplate && !actor.IsAdmin) return Forbid(); // system sets are Admin-managed

        var now = DateTimeOffset.UtcNow;
        var t = new PrescriptionTemplate
        {
            TemplateId = Guid.NewGuid(), DoctorId = req.DoctorId, Title = req.Title,
            IsSystemTemplate = req.IsSystemTemplate, CreatedAt = now, UpdatedAt = now
        };
        db.PrescriptionTemplates.Add(t);
        foreach (var i in req.Items)
            db.PrescriptionTemplateItems.Add(new PrescriptionTemplateItem
            {
                Id = Guid.NewGuid(), TemplateId = t.TemplateId, MedicineId = i.MedicineId,
                GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
                Instruction = i.Instruction, IsControlledSubstance = i.IsControlledSubstance, CreatedAt = now
            });
        await db.SaveChangesAsync(ct);
        return Ok(await db.PrescriptionTemplates.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.TemplateId == t.TemplateId, ct));
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("prescription-templates/{id:guid}")]
    public async Task<ActionResult<PrescriptionTemplate>> UpdateTemplate(Guid id, UpsertRxTemplateRequest req, CancellationToken ct)
    {
        var t = await db.PrescriptionTemplates.Include(x => x.Items).SingleOrDefaultAsync(x => x.TemplateId == id, ct);
        if (t is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(t.DoctorId) || ((t.IsSystemTemplate || req.IsSystemTemplate) && !actor.IsAdmin)) return Forbid();

        var now = DateTimeOffset.UtcNow;
        t.Title = req.Title;
        t.IsSystemTemplate = req.IsSystemTemplate;
        t.UpdatedAt = now;
        db.PrescriptionTemplateItems.RemoveRange(t.Items);
        foreach (var i in req.Items)
            db.PrescriptionTemplateItems.Add(new PrescriptionTemplateItem
            {
                Id = Guid.NewGuid(), TemplateId = t.TemplateId, MedicineId = i.MedicineId,
                GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
                Instruction = i.Instruction, IsControlledSubstance = i.IsControlledSubstance, CreatedAt = now
            });
        await db.SaveChangesAsync(ct);
        return Ok(await db.PrescriptionTemplates.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.TemplateId == t.TemplateId, ct));
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpDelete("prescription-templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        var t = await db.PrescriptionTemplates.SingleOrDefaultAsync(x => x.TemplateId == id, ct);
        if (t is null) return NotFound();
        var actor = await actors.ResolveAsync(User, ct);
        if (!actor.ActsAsDoctor(t.DoctorId) || (t.IsSystemTemplate && !actor.IsAdmin)) return Forbid();
        db.PrescriptionTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── doctor_favorite_medicines ─────────────────────────────────────────
    [Authorize(Roles = "Doctor,Admin")]
    [HttpGet("doctor-favorite-medicines")]
    public async Task<ActionResult<List<DoctorFavoriteMedicine>>> GetFavorites([FromQuery] Guid doctorId, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        return Ok(await db.DoctorFavoriteMedicines.AsNoTracking().Where(f => f.DoctorId == doctorId).OrderBy(f => f.GenericName).ToListAsync(ct));
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpPost("doctor-favorite-medicines")]
    public async Task<ActionResult<DoctorFavoriteMedicine>> AddFavorite([FromQuery] Guid doctorId, FavoriteMedicineInput input, CancellationToken ct)
    {
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();
        var f = new DoctorFavoriteMedicine
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, MedicineId = input.MedicineId,
            GenericName = input.GenericName, Dosage = input.Dosage, Quantity = input.Quantity,
            Instruction = input.Instruction, CreatedAt = DateTimeOffset.UtcNow
        };
        db.DoctorFavoriteMedicines.Add(f);
        await db.SaveChangesAsync(ct);
        return Ok(f);
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpPut("doctor-favorite-medicines/{id:guid}")]
    public async Task<ActionResult<DoctorFavoriteMedicine>> UpdateFavorite(Guid id, FavoriteMedicineInput input, CancellationToken ct)
    {
        var f = await db.DoctorFavoriteMedicines.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound();
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(f.DoctorId)) return Forbid();
        f.MedicineId = input.MedicineId;
        f.GenericName = input.GenericName;
        f.Dosage = input.Dosage;
        f.Quantity = input.Quantity;
        f.Instruction = input.Instruction;
        await db.SaveChangesAsync(ct);
        return Ok(f);
    }

    [Authorize(Roles = "Doctor,Admin")]
    [HttpDelete("doctor-favorite-medicines/{id:guid}")]
    public async Task<IActionResult> DeleteFavorite(Guid id, CancellationToken ct)
    {
        var f = await db.DoctorFavoriteMedicines.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound();
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(f.DoctorId)) return Forbid();
        db.DoctorFavoriteMedicines.Remove(f);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
