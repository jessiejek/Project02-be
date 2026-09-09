using Microsoft.AspNetCore.Identity;

namespace ClinicApp.Auth;

/// <summary>Thin wrapper around ASP.NET Core Identity's PasswordHasher — no extra NuGet package needed.</summary>
public class PasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string hash, string password) =>
        _hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
}
