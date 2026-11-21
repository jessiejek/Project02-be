//JJQR - AI (Nov 21, 2026) cloud sync: node secrets and node access tokens
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClinicApp.Api.Sync;

public sealed class SyncNodeAuth(IOptions<JwtOptions> options)
{
    public const string Role = "SyncNode";
    public const string NodeIdClaim = "node_id";
    public const int TokenMinutes = 60;

    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool Verify(string secret, string hash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(hash));

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(Guid nodeId)
    {
        var o = options.Value;
        var expires = DateTimeOffset.UtcNow.AddMinutes(TokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, nodeId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, nodeId.ToString()),
            new Claim(ClaimTypes.Role, Role),
            new Claim(NodeIdClaim, nodeId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(o.Issuer, o.Audience, claims, expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    /// <summary>Called from JwtBearer OnTokenValidated for node tokens. Returns a failure reason or null.</summary>
    public static async Task<string?> ValidateAsync(ClaimsPrincipal principal, HttpContext http)
    {
        if (!http.Request.Path.StartsWithSegments("/api/sync")) return "Node tokens are only valid for /api/sync.";
        if (!Guid.TryParse(principal.FindFirst(NodeIdClaim)?.Value, out var nodeId)) return "Invalid node token.";
        var db = http.RequestServices.GetRequiredService<ClinicAppDbContext>();
        var node = await db.SyncNodes.AsNoTracking().FirstOrDefaultAsync(n => n.NodeId == nodeId, http.RequestAborted);
        if (node is null) return "Unknown node.";
        if (node.Status != SyncNodeStatus.Active)
        {
            http.Items["sync_node_revoked"] = true;
            return "Node revoked.";
        }
        return null;
    }
}
//JJQR - END
