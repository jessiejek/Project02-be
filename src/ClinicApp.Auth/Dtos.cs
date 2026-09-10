using System.Text.Json.Serialization;

namespace ClinicApp.Auth;

// These DTOs are camelCase on the wire — verified against the actual Angular source
// (src/app/core/services/auth.service.ts AuthSessionDto/AuthUserDto/RefreshTokenDto and every
// auth/* page). This is DIFFERENT from the snake_case data-resource contract (patients, bookings,
// etc.) — auth is a new custom-built layer, not a Supabase table passthrough, so [JsonPropertyName]
// is set explicitly here to override the API's global snake_case naming policy.

public record LoginRequest(string Email, string Password);

public record RegisterRequest(
    [property: JsonPropertyName("firstName")] string FirstName,
    [property: JsonPropertyName("middleName")] string? MiddleName,
    [property: JsonPropertyName("lastName")] string LastName,
    string Email,
    string Password);

public record RefreshTokenRequest([property: JsonPropertyName("refreshToken")] string RefreshToken);

public record LogoutRequest([property: JsonPropertyName("refreshToken")] string? RefreshToken);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(
    string Email,
    string Token,
    [property: JsonPropertyName("newPassword")] string NewPassword);

public record SetPasswordRequest(
    [property: JsonPropertyName("newPassword")] string NewPassword,
    [property: JsonPropertyName("confirmPassword")] string ConfirmPassword);

public record ChangePasswordRequest(
    [property: JsonPropertyName("currentPassword")] string CurrentPassword,
    [property: JsonPropertyName("newPassword")] string NewPassword);

public record ResendVerificationRequest(string Email);

public record GoogleLoginRequest(
    string Provider,
    [property: JsonPropertyName("idToken")] string? IdToken,
    [property: JsonPropertyName("accessToken")] string AccessToken);

public record FacebookLoginRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("userId")] string UserId);

public record InviteRequest(string Email, [property: JsonPropertyName("fullName")] string FullName, string Role);

public class AuthUserDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; set; }
    [JsonPropertyName("isFirstLogin")] public bool IsFirstLogin { get; set; }
    [JsonPropertyName("phoneNumber")] public string? PhoneNumber { get; set; }
}

public class AuthSessionDto
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("user")] public AuthUserDto User { get; set; } = new();
}

public class RefreshTokenResponseDto
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
}
