using ClinicApp.Api.Security;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicApp.Api.Controllers;

//JJQR - AI (Nov 20, 2026) A4
[ApiController]
[Authorize(Roles = "Doctor,Admin")]
[Route("api/templates")]
public class TemplatesController(ActorResolver actors, BuiltInTemplateCopier copier) : ControllerBase
{
    /// <summary>Re-adds built-in templates of <paramref name="kind"/> that this doctor deleted.
    /// Never overwrites her existing rows. kind=favorite has no built-ins (400).</summary>
    [HttpPost("restore-built-ins")]
    public async Task<IActionResult> RestoreBuiltIns([FromQuery] Guid doctorId, [FromQuery] string? kind, CancellationToken ct)
    {
        if (doctorId == Guid.Empty) return BadRequest(new { message = "doctorId is required." });
        if (!(await actors.ResolveAsync(User, ct)).ActsAsDoctor(doctorId)) return Forbid();

        var k = BuiltInTemplateKind.Normalize(kind);
        if (k is null) return BadRequest(new { message = "kind must be soap, rx, cert, diagnosis, phrase, or favorite." });
        if (k == BuiltInTemplateKind.Favorite)
            return BadRequest(new { restored = 0, kind = k, message = "Favorite medicines have no built-in set." });

        var restored = await copier.RestoreAsync(doctorId, k, ct);
        return Ok(new { restored, kind = k });
    }
}
//JJQR - END
