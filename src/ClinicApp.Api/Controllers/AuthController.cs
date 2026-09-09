using System.Security.Claims;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

/// <summary>
/// All routes here are camelCase on the wire (see ClinicApp.Auth.Dtos) — verified against the
/// actual Angular calls (auth/login, auth/register, auth/refresh-token, auth/logout, auth/me,
/// auth/forgot-password, auth/reset-password, auth/set-password, auth/avatar, auth/google,
/// auth/facebook). This is intentionally different from the snake_case data-resource controllers.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    ClinicAppDbContext db,
    ITokenService tokenService,
    PasswordHasherService passwordHasher,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<AuthSessionDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email.Trim().ToLower(), ct);
        if (user is null || !passwordHasher.Verify(user, user.PasswordHash, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var session = await BuildSessionAsync(user, ct);
        if (session is null)
        {
            return Problem("User has no profile/role assigned.", statusCode: 500);
        }

        return Ok(session);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthSessionDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLower();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = email, EmailConfirmed = false, CreatedAt = now };
        user.PasswordHash = passwordHasher.Hash(user, request.Password);

        db.Users.Add(user);
        db.Profiles.Add(new Profile { Id = user.Id, Role = UserRole.Patient, CreatedAt = now });

        var patientCode = $"P-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        db.Patients.Add(new Patient
        {
            PatientId = Guid.NewGuid(),
            UserId = user.Id,
            PatientCode = patientCode,
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = DateOnly.FromDateTime(now.Date), // TODO: FE register form doesn't collect DOB yet — placeholder until it does.
            Sex = SexType.Female, // TODO: same — no sex field on the register form today.
            Email = email,
            IsGuest = false,
            IsEmailVerified = false,
            ConsentVersion = 1,
            ConsentedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(ct);

        var session = await BuildSessionAsync(user, ct);
        return Ok(session);
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<RefreshTokenResponseDto>> RefreshToken(RefreshTokenRequest request, CancellationToken ct)
    {
        var hash = tokenService.HashToken(request.RefreshToken);
        var existing = await db.RefreshTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null || !existing.IsActive)
        {
            return Unauthorized(new { message = "Refresh token is invalid or expired." });
        }

        // Rotate: revoke the old token, issue a new pair.
        existing.RevokedAt = DateTimeOffset.UtcNow;

        var profile = await db.Profiles.FindAsync([existing.UserId], ct);
        var role = profile?.Role.ToString() ?? "Patient";

        var (accessToken, refreshTokenValue) = await IssueTokenPairAsync(existing.UserId, role, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new RefreshTokenResponseDto { AccessToken = accessToken, RefreshToken = refreshTokenValue });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.RefreshToken))
        {
            var hash = tokenService.HashToken(request.RefreshToken);
            var existing = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (existing is not null)
            {
                existing.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        return Ok();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await db.Users.FindAsync([userId.Value], ct);
        if (user is null) return Unauthorized();

        var dto = await BuildUserDtoAsync(user, ct);
        return dto is null ? Problem("User has no profile/role assigned.", statusCode: 500) : Ok(dto);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLower();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // Always return 200 regardless of whether the email exists — don't leak account existence.
        if (user is not null)
        {
            var token = tokenService.CreatePurposeToken(user.Id, "password-reset", TimeSpan.FromMinutes(30));
            var clientBaseUrl = configuration["ClientBaseUrl"] ?? "http://localhost:4200";
            var resetLink = $"{clientBaseUrl}/auth/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";

            // TODO: wire real SMTP send using the Email:* settings in appsettings.json.
            // Logged instead of emailed until SMTP credentials are configured.
            Console.WriteLine($"[AuthController] Password reset link for {email}: {resetLink}");
        }

        return Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        if (!tokenService.TryValidatePurposeToken(request.Token, "password-reset", out var userId))
        {
            return BadRequest(new { message = "This reset link is invalid or has expired." });
        }

        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || !string.Equals(user.Email, request.Email.Trim().ToLower(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "This reset link is invalid or has expired." });
        }

        user.PasswordHash = passwordHasher.Hash(user, request.NewPassword);
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "Password reset successfully." });
    }

    [Authorize]
    [HttpPost("set-password")]
    public async Task<ActionResult<AuthUserDto>> SetPassword(SetPasswordRequest request, CancellationToken ct)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            return BadRequest(new { message = "Passwords do not match." });
        }

        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await db.Users.FindAsync([userId.Value], ct);
        if (user is null) return Unauthorized();

        user.PasswordHash = passwordHasher.Hash(user, request.NewPassword);
        user.MustSetPassword = false;
        await db.SaveChangesAsync(ct);

        var dto = await BuildUserDtoAsync(user, ct);
        return dto is null ? Problem("User has no profile/role assigned.", statusCode: 500) : Ok(dto);
    }

    [Authorize]
    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile file, [FromServices] ClinicApp.Infrastructure.Files.IFileStorageService fileStorage, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var staff = await db.StaffAccounts.SingleOrDefaultAsync(s => s.UserId == userId.Value, ct);
        if (staff is null)
        {
            return BadRequest(new { message = "Only staff/doctor/admin accounts have avatars." });
        }

        await using var stream = file.OpenReadStream();
        var url = await fileStorage.SaveAsync(staff.StaffId, staff.StaffId, file.FileName, stream, file.ContentType, ct);

        staff.AvatarUrl = url;
        await db.SaveChangesAsync(ct);

        return Ok(new { avatarUrl = url });
    }

    /// <summary>Admin/Staff only. Creates an invited account (StaffAccount.Status = Invited,
    /// User.MustSetPassword = true); the invitee completes setup via auth/set-password.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("invite")]
    public async Task<IActionResult> Invite(InviteRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLower();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        if (!Enum.TryParse<StaffRole>(request.Role, out var staffRole))
        {
            return BadRequest(new { message = "Role must be Staff, Doctor, or Admin." });
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = email, EmailConfirmed = false, MustSetPassword = true, CreatedAt = now };
        // Random unguessable placeholder password; overwritten when the invitee calls set-password.
        user.PasswordHash = passwordHasher.Hash(user, Guid.NewGuid().ToString("N") + Guid.NewGuid());

        db.Users.Add(user);
        db.Profiles.Add(new Profile { Id = user.Id, Role = staffRole switch
        {
            StaffRole.Doctor => UserRole.Doctor,
            StaffRole.Admin => UserRole.Admin,
            _ => UserRole.Staff
        }, CreatedAt = now });

        var staffId = Guid.NewGuid();
        db.StaffAccounts.Add(new StaffAccount
        {
            StaffId = staffId,
            UserId = user.Id,
            FullName = request.FullName,
            Email = email,
            Role = staffRole,
            Status = StaffStatus.Invited,
            InvitedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync(ct);

        // TODO: send invite email with a set-password link once SMTP is wired (see forgot-password).
        return Ok(new { userId = user.Id, staffId });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();

        db.Users.Remove(user); // cascades to Profile/Patient/StaffAccount FKs per DB constraints
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Social login: NOT wired up yet ─────────────────────────────────────
    // Verifying the Google id_token / Facebook access_token server-side needs an HTTP call out to
    // Google's tokeninfo endpoint / Facebook's Graph API (or a library) before trusting the claims.
    // Left as a documented 501 rather than silently trusting an unverified client-supplied token.

    [HttpPost("google")]
    public IActionResult GoogleLogin(GoogleLoginRequest request) =>
        Problem("Google login is not implemented on the backend yet.", statusCode: StatusCodes.Status501NotImplemented);

    [HttpPost("facebook")]
    public IActionResult FacebookLogin(FacebookLoginRequest request) =>
        Problem("Facebook login is not implemented on the backend yet.", statusCode: StatusCodes.Status501NotImplemented);

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private async Task<AuthSessionDto?> BuildSessionAsync(User user, CancellationToken ct)
    {
        var dto = await BuildUserDtoAsync(user, ct);
        if (dto is null) return null;

        var profile = await db.Profiles.FindAsync([user.Id], ct);
        var (accessToken, refreshTokenValue) = await IssueTokenPairAsync(user.Id, profile!.Role.ToString(), ct);
        await db.SaveChangesAsync(ct);

        return new AuthSessionDto { AccessToken = accessToken, RefreshToken = refreshTokenValue, User = dto };
    }

    private async Task<AuthUserDto?> BuildUserDtoAsync(User user, CancellationToken ct)
    {
        var profile = await db.Profiles.FindAsync([user.Id], ct);
        if (profile is null) return null;

        if (profile.Role == UserRole.Patient)
        {
            var patient = await db.Patients.SingleOrDefaultAsync(p => p.UserId == user.Id, ct);
            return new AuthUserDto
            {
                Id = user.Id.ToString(),
                FullName = patient is null ? user.Email : $"{patient.FirstName} {patient.LastName}",
                Email = user.Email,
                Role = profile.Role.ToString(),
                AvatarUrl = null,
                IsFirstLogin = user.MustSetPassword,
                PhoneNumber = patient?.ContactNumber
            };
        }

        var staff = await db.StaffAccounts.SingleOrDefaultAsync(s => s.UserId == user.Id, ct);
        return new AuthUserDto
        {
            Id = user.Id.ToString(),
            FullName = staff?.FullName ?? user.Email,
            Email = user.Email,
            Role = profile.Role.ToString(),
            AvatarUrl = staff?.AvatarUrl,
            IsFirstLogin = user.MustSetPassword,
            PhoneNumber = staff?.ContactNumber
        };
    }

    /// <summary>Issues a new access+refresh pair and stages the refresh token row (caller must
    /// still call SaveChangesAsync).</summary>
    private async Task<(string accessToken, string refreshTokenValue)> IssueTokenPairAsync(Guid userId, string role, CancellationToken ct)
    {
        var accessToken = tokenService.CreateAccessToken(userId, role);
        var refreshTokenValue = tokenService.GenerateRefreshTokenValue();
        var refreshExpiryDays = configuration.GetValue<int?>("Jwt:RefreshTokenExpiryDays") ?? 7;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenService.HashToken(refreshTokenValue),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(refreshExpiryDays),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Task.CompletedTask;
        return (accessToken, refreshTokenValue);
    }
}
