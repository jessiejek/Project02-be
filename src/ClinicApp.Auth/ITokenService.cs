namespace ClinicApp.Auth;

public interface ITokenService
{
    /// <summary>Issues a signed access token. sub = userId, role = user's Profile.Role string (e.g. "Patient").</summary>
    string CreateAccessToken(Guid userId, string role);

    /// <summary>Generates a cryptographically random opaque refresh token (the raw value returned to the client).</summary>
    string GenerateRefreshTokenValue();

    /// <summary>One-way hash of a refresh token value, for storage/lookup (never store the raw token).</summary>
    string HashToken(string rawToken);

    /// <summary>Signed, short-lived, single-purpose token (e.g. password reset links) — stateless,
    /// no DB row needed. purpose is embedded as a claim and re-checked on validation.</summary>
    string CreatePurposeToken(Guid userId, string purpose, TimeSpan ttl);

    /// <summary>Validates a purpose token created by CreatePurposeToken. Returns false if expired,
    /// malformed, or the purpose claim doesn't match.</summary>
    bool TryValidatePurposeToken(string token, string expectedPurpose, out Guid userId);
}
