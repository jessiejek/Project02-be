namespace ClinicApp.Auth;

/// <summary>Replaces Supabase auth.users. Profile.Id / Patient.UserId / StaffAccount.UserId are FKs to this.</summary>
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool EmailConfirmed { get; set; }
    /// <summary>True for invited staff/doctor/admin accounts until they complete auth/set-password
    /// (maps to AuthUserDto.isFirstLogin, which firstLoginGuard checks on the frontend).</summary>
    public bool MustSetPassword { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Store a hash of the token, never the raw value.</summary>
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
