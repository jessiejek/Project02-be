namespace ClinicApp.Auth;

/// <summary>Bound from appsettings.json "Jwt" section.</summary>
public class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AccessTokenExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
