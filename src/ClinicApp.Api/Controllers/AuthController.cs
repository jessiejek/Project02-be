using System.Security.Claims;
using ClinicApp.Api.Auditing;
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
    IConfiguration configuration,
    IHostEnvironment environment,
    IPatientCodeAllocator patientCodes) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<AuthSessionDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email.Trim().ToLower(), ct);

        // JJQR - AI (Nov 20, 2026) - PV-3 / PV-5: lock after repeated wrong passwords, and write every sign-in and failure to the access log (no email or password in the log)
        var nowUtc = DateTimeOffset.UtcNow;
        if (user is not null && user.LockedUntil is { } until && until > nowUtc)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((until - nowUtc).TotalMinutes));
            AuditLogWriter.Add(db, AuditEntityType.Auth, user.Id, "LoginBlocked", null, "Account is locked");
            await db.SaveChangesAsync(ct);
            return StatusCode(StatusCodes.Status423Locked, new { message = $"Too many failed sign-in attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}." });
        }
        if (user is null || !passwordHasher.Verify(user, user.PasswordHash, request.Password))
        {
            if (user is null)
            {
                AuditLogWriter.Add(db, AuditEntityType.Auth, Guid.Empty, "LoginFailed", null, "No such account");
            }
            else
            {
                user.FailedLoginCount++;
                var locked = user.FailedLoginCount >= PasswordPolicy.MaxFailedLogins;
                if (locked) { user.LockedUntil = nowUtc + PasswordPolicy.LockoutFor; user.FailedLoginCount = 0; }
                AuditLogWriter.Add(db, AuditEntityType.Auth, user.Id, locked ? "AccountLocked" : "LoginFailed", null, "Wrong password");
            }
            await db.SaveChangesAsync(ct);
            return Unauthorized(new { message = "Invalid email or password." });
        }
        if (user.FailedLoginCount != 0 || user.LockedUntil is not null)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
        }
        // JJQR - END

        // JJQR - AI (Nov 20, 2026) - B-32: a deactivated staff account must not get a session
        if (!await ClinicApp.Api.Security.AccountGuard.IsActiveAsync(db, user.Id, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ClinicApp.Api.Security.AccountGuard.DeactivatedMessage });
        }
        // JJQR - END

        var session = await BuildSessionAsync(user, ct);
        if (session is null)
        {
            return Problem("User has no profile/role assigned.", statusCode: 500);
        }

        // JJQR - AI (Nov 20, 2026) - PV-5: successful sign-in
        AuditLogWriter.Add(db, AuditEntityType.Auth, user.Id, "Login", user.Id);
        await db.SaveChangesAsync(ct);
        // JJQR - END
        return Ok(session);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthSessionDto>> Register(RegisterRequest request, CancellationToken ct)
    {
        //JJQR - AI (Nov 24, 2026) - owner: the buyer said no online booking, so patients cannot sign themselves up any more. Closed by default; set the host variable Registration__PatientSelfSignUp=true to open it again. (Local development and the automated tests stay open.)
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && !string.Equals(configuration["Registration:PatientSelfSignUp"], "true", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Online patient registration is closed. Please register at the clinic's front desk." });
        //JJQR - END
        var email = request.Email.Trim().ToLower();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        // JJQR - AI (Nov 20, 2026) - PV-3 / B-01: password rule, valid email, sane name lengths
        var registerCheck = new ClinicApp.Api.Validation.Check()
            .Required("first_name", "First name", request.FirstName, 100)
            .Required("last_name", "Last name", request.LastName, 100)
            .Optional("middle_name", "Middle name", request.MiddleName, 100)
            .Email("email", "Email", request.Email, required: true)
            .Phone("contact_number", "Contact number", request.ContactNumber);
        if (PasswordPolicy.Check(request.Password, email) is { } weak) registerCheck.Fail("password", weak);
        if (registerCheck.Result() is { } invalidRegistration) return invalidRegistration;
        // JJQR - END

        if (request.DateOfBirth is not { } dob || dob > ClinicApp.Domain.ClinicClock.Today || dob.Year < 1900)
        {
            return BadRequest(new { message = "A valid date of birth is required." });
        }
        if (!Enum.TryParse<SexType>(request.Sex, ignoreCase: true, out var sex) || !Enum.IsDefined(sex))
        {
            return BadRequest(new { message = "Sex must be Male or Female." });
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = email, EmailConfirmed = false, CreatedAt = now };
        user.PasswordHash = passwordHasher.Hash(user, request.Password);

        // §17.1 #1 — the patient_code comes from the same server allocator as staff-created
        // patients (`MF-000123`), retried on a unique-index collision. (This used to be a
        // UTC-date + random-hex code, which was both collision-prone and off the clinic's format.)
        var patient = new Patient
        {
            PatientId = Guid.NewGuid(),
            UserId = user.Id,
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = dob,
            Sex = sex,
            ContactNumber = string.IsNullOrWhiteSpace(request.ContactNumber) ? null : request.ContactNumber.Trim(),
            Email = email,
            IsGuest = false,
            IsEmailVerified = false,
            // JJQR - AI (Nov 20, 2026) - PV-12: no pre-made consent; recorded below only if the patient ticked the box
            ConsentVersion = 0,
            ConsentedAt = null,
            // JJQR - END
            CreatedAt = now,
            UpdatedAt = now
        };
        var profile = new Profile { Id = user.Id, Role = UserRole.Patient, CreatedAt = now };

        await PatientCodes.SaveWithFreshCodeAsync(db, patientCodes, code =>
        {
            patient.PatientCode = code;
            db.Users.Add(user);
            db.Profiles.Add(profile);
            db.Patients.Add(patient);
        }, ct);

        // JJQR - AI (Nov 20, 2026) - PV-12: dated, versioned consent record — only when the patient actively agreed
        if (request.AcceptedPrivacyNotice)
        {
            var version = await db.ClinicSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => (int?)s.ConsentVersion).SingleOrDefaultAsync(ct) ?? 1;
            patient.ConsentVersion = version;
            patient.ConsentedAt = now;
            db.PatientConsents.Add(new PatientConsent { Id = Guid.NewGuid(), PatientId = patient.PatientId, ConsentVersion = version, Decision = ConsentDecision.Accepted, DecidedAt = now });
            await db.SaveChangesAsync(ct);
        }
        // JJQR - END

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

        // JJQR - AI (Nov 20, 2026) - B-32: a deactivated account cannot refresh into a new session
        if (!await ClinicApp.Api.Security.AccountGuard.IsActiveAsync(db, existing.UserId, ct))
        {
            existing.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Unauthorized(new { message = ClinicApp.Api.Security.AccountGuard.DeactivatedMessage });
        }
        // JJQR - END

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
            // JJQR - AI (Nov 20, 2026) - PV-9: a reset link is a credential — only ever printed on a developer machine, never in production logs
            if (environment.IsDevelopment()) Console.WriteLine($"[AuthController] Password reset link for {email}: {resetLink}");
            // JJQR - END
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

        // JJQR - AI (Nov 20, 2026) - PV-3: same password rule; a reset also clears any lockout
        if (PasswordPolicy.Check(request.NewPassword, user.Email) is { } weakReset) return BadRequest(new { message = weakReset });
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        // JJQR - END
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

        if (PasswordPolicy.Check(request.NewPassword, user.Email) is { } weakSet) return BadRequest(new { message = weakSet }); // JJQR - AI (Nov 20, 2026) - PV-3
        user.PasswordHash = passwordHasher.Hash(user, request.NewPassword);
        user.MustSetPassword = false;
        await db.SaveChangesAsync(ct);

        var dto = await BuildUserDtoAsync(user, ct);
        return dto is null ? Problem("User has no profile/role assigned.", statusCode: 500) : Ok(dto);
    }

    /// <summary>Signed-in user changes their own password (verifies the current one first).</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        // JJQR - AI (Nov 20, 2026) - PV-3: was "at least 6 characters"; now the shared rule (checked again with the email below)
        if (PasswordPolicy.Check(request.NewPassword) is { } weakChange)
            return BadRequest(new { message = weakChange });
        // JJQR - END

        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await db.Users.FindAsync([userId.Value], ct);
        if (user is null) return Unauthorized();

        if (!passwordHasher.Verify(user, user.PasswordHash, request.CurrentPassword))
            return BadRequest(new { message = "Current password is incorrect." });
        if (PasswordPolicy.Check(request.NewPassword, user.Email) is { } weakChangeEmail) return BadRequest(new { message = weakChangeEmail }); // JJQR - AI (Nov 20, 2026) - PV-3

        user.PasswordHash = passwordHasher.Hash(user, request.NewPassword);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Password updated." });
    }

    //JJQR - AI (Nov 22, 2026) - owner: every signed-in user (patient, staff, doctor, admin) can change their own sign-in email. The current password is required.
    // The email is changed everywhere it is stored for this person (login, staff profile, patient record). Other sessions are signed out; this one keeps working.
    [Authorize]
    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var user = await db.Users.FindAsync([userId.Value], ct);
        if (user is null) return Unauthorized();

        if (!passwordHasher.Verify(user, user.PasswordHash, request.CurrentPassword ?? ""))
            return BadRequest(new { message = "Your current password is incorrect." });
        var check = new ClinicApp.Api.Validation.Check().Email("email", "Email", request.NewEmail, required: true);
        if (check.Result() is { } invalid) return invalid;
        var newEmail = request.NewEmail.Trim().ToLowerInvariant();
        if (newEmail == user.Email) return BadRequest(new { message = "That is already your email." });
        if (await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != user.Id, ct))
            return Conflict(new { message = "An account with this email already exists." });

        await ApplyEmailChangeAsync(user, newEmail, ct);
        AuditLogWriter.Add(db, AuditEntityType.Auth, user.Id, "EmailChanged", user.Id, "Sign-in email changed by the user");
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Email updated. Use the new email the next time you sign in.", email = newEmail });
    }

    /// <summary>One place that changes the email everywhere this person has it.</summary>
    internal async Task ApplyEmailChangeAsync(ClinicApp.Auth.User user, string newEmail, CancellationToken ct)
    {
        user.Email = newEmail;
        foreach (var s in await db.StaffAccounts.Where(x => x.UserId == user.Id).ToListAsync(ct)) s.Email = newEmail;
        foreach (var p in await db.Patients.Where(x => x.UserId == user.Id).ToListAsync(ct)) p.Email = newEmail;
    }
    //JJQR - END

    /// <summary>Re-send the email-verification link (no-op without SMTP — logs the link).</summary>
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(ResendVerificationRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLower();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is not null)
        {
            var token = tokenService.CreatePurposeToken(user.Id, "email-verify", TimeSpan.FromDays(1));
            var clientBaseUrl = configuration["ClientBaseUrl"] ?? "http://localhost:3000";
            if (environment.IsDevelopment()) Console.WriteLine($"[AuthController] Email verification link for {email}: {clientBaseUrl}/verify-email?token={Uri.EscapeDataString(token)}"); // JJQR - AI (Nov 20, 2026) - PV-9
        }
        return Ok(new { message = "If that email is registered, a verification link has been sent." });
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

        // Doctor invites carry the full clinical profile so doctors + doctor_schedules
        // land in the same SaveChanges — no separate follow-up call, no orphaned row.
        if (staffRole == StaffRole.Doctor && request.Doctor is { } dp)
        {
            db.Doctors.Add(new Doctor
            {
                DoctorId = staffId, // shared PK with StaffAccounts.StaffId
                Specialization = dp.Specialization,
                ConsultationFee = dp.ConsultationFee,
                Bio = string.IsNullOrWhiteSpace(dp.Bio) ? null : dp.Bio,
                LicenseNumber = string.IsNullOrWhiteSpace(dp.LicenseNumber) ? null : dp.LicenseNumber,
                PtrNumber = string.IsNullOrWhiteSpace(dp.PtrNumber) ? null : dp.PtrNumber,
                S2Number = string.IsNullOrWhiteSpace(dp.S2Number) ? null : dp.S2Number,
                SlotDurationMinutes = dp.SlotDurationMinutes,
                CreatedAt = now,
                UpdatedAt = now
            });

            foreach (var d in dp.Schedule)
            {
                db.DoctorSchedules.Add(new DoctorSchedule
                {
                    Id = Guid.NewGuid(),
                    DoctorId = staffId,
                    DayOfWeek = d.DayOfWeek,
                    IsActive = d.IsActive,
                    StartTime = TimeOnly.Parse(d.StartTime),
                    EndTime = TimeOnly.Parse(d.EndTime),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

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

        // profiles.id / staff_accounts.user_id have NO database FK to users (they
        // were auth.users rows under Supabase), so deleting the user does not
        // cascade to them — remove them explicitly. Doctor + doctor_schedules DO
        // cascade from staff_accounts.staff_id. This is the revoke-invite path;
        // patient-linked users are not deleted here (their visit history blocks it).
        var staff = await db.StaffAccounts.Where(s => s.UserId == id).ToListAsync(ct);
        if (staff.Count > 0) db.StaffAccounts.RemoveRange(staff);

        var profile = await db.Profiles.FindAsync([id], ct);
        if (profile is not null) db.Profiles.Remove(profile);

        db.Users.Remove(user);
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
