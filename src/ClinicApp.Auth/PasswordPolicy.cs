// JJQR - AI (Nov 20, 2026) - PV-3: one password rule for register / reset / set / change (RA 10173: appropriate security measures)
namespace ClinicApp.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    /// <summary>Whole-password matches only (case-insensitive), so a long passphrase that happens to contain a common word is still fine.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234", "password12345", "password123456", "passw0rd1234",
        "123456789012", "1234567890123", "12345678901234", "111111111111", "000000000000", "123123123123", "123412341234", "abcdefghijkl",
        "qwertyuiop12", "qwertyuiopas", "qwertyuiop123", "qwerty123456", "qwertyqwerty", "asdfghjkl123", "asdfghjklqwe", "zxcvbnm12345",
        "iloveyou1234", "iloveyou12345", "iloveyou2020", "letmein12345", "welcome12345", "welcome1234!", "administrator", "administrator1",
        "admin1234567", "admin12345678", "admin123456!", "changeme1234", "changeme12345", "changemenow1", "clinic123456", "clinic1234567",
        "clinicadmin1", "clinicpassword", "doctor123456", "doctor1234567", "patient123456", "patient1234567", "staff12345678", "gavino123456",
        "philippines1", "philippines12", "pilipinas1234", "pilipinas12345", "mahalkita123", "iloveyoupo12", "kapamilya123", "maganda12345",
        "manila123456", "cebu12345678", "davao1234567", "dragon123456", "monkey123456", "football1234", "baseball1234", "superman1234",
        "trustno12345", "sunshine1234", "princess1234", "whatever1234", "starwars1234", "master123456", "shadow123456", "michael12345",
        "jennifer1234", "charlie12345", "computer1234", "internet1234", "freedom12345", "pass12345678", "password!234", "p@ssw0rd1234",
        "p@ssword1234", "p@ssw0rd12345", "passw0rd!234", "abc123456789", "abcd12345678", "1q2w3e4r5t6y", "1qaz2wsx3edc", "1qazxsw23edc",
        "q1w2e3r4t5y6", "zaq12wsxcde3", "aaaaaaaaaaaa", "bbbbbbbbbbbb", "123456abcdef", "a1b2c3d4e5f6", "test12345678", "testtesttest",
        "guest1234567", "default12345", "secret123456", "mypassword123", "mypassword1234", "newpassword12", "newpassword123", "letmein123456",
        "welcome123456", "login1234567", "hello1234567", "helloworld123", "qazwsxedc123",
    };

    /// <summary>Null when the password is acceptable, otherwise the sentence to show the user (it never echoes the password).</summary>
    public static string? Check(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (password.Length > MaxLength)
            return $"Password must be at most {MaxLength} characters.";
        if (password.Trim().Length == 0)
            return "Password cannot be only spaces.";
        if (Common.Contains(password.Trim()) || password.Distinct().Count() <= 2)
            return "That password is too common. Choose something harder to guess, such as a few unrelated words.";
        if (!string.IsNullOrWhiteSpace(email))
        {
            var local = email.Trim().Split('@')[0];
            if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
                return "Password cannot contain your email name.";
        }
        return null;
    }
}
// JJQR - END
