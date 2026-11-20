// JJQR - AI (Nov 20, 2026) - B-01: one small helper so every controller validates input the same way and answers a clear 400
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace ClinicApp.Api.Validation;

/// <summary>Collects field errors and turns them into <c>400 { message, errors }</c>. <c>message</c> is the
/// human sentence the UI shows (the field errors joined); <c>errors</c> maps field → message.
/// Messages never echo the submitted value back (no patient data in error text).</summary>
public sealed partial class Check
{
    private readonly Dictionary<string, string> _errors = new();

    public bool Ok => _errors.Count == 0;

    public Check Fail(string field, string message)
    {
        _errors.TryAdd(field, message);
        return this;
    }

    /// <summary>Required text: not blank, at most <paramref name="max"/> characters.</summary>
    public Check Required(string field, string label, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return Fail(field, $"{label} is required.");
        if (value.Trim().Length > max) return Fail(field, $"{label} is too long (max {max} characters).");
        return this;
    }

    /// <summary>Optional text: when present, at most <paramref name="max"/> characters.</summary>
    public Check Optional(string field, string label, string? value, int max)
    {
        if (!string.IsNullOrEmpty(value) && value.Trim().Length > max) Fail(field, $"{label} is too long (max {max} characters).");
        return this;
    }

    /// <summary>Optional phone: digits with + ( ) - . and spaces, 7–20 characters.</summary>
    public Check Phone(string field, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        var v = value.Trim();
        if (!PhoneRx().IsMatch(v) || v.Count(char.IsDigit) < 7) Fail(field, $"{label} must be a phone number (digits, spaces, + - ( ) only).");
        return this;
    }

    public Check Email(string field, string label, string? value, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return required ? Fail(field, $"{label} is required.") : this;
        var v = value.Trim();
        if (v.Length > 254 || !EmailRx().IsMatch(v)) Fail(field, $"{label} must be a valid email address.");
        return this;
    }

    public Check Hex(string field, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !HexRx().IsMatch(value.Trim())) Fail(field, $"{label} must be a colour like #1a2b3c.");
        return this;
    }

    public Check Money(string field, string label, decimal value, decimal max = 1_000_000m)
    {
        if (value < 0) return Fail(field, $"{label} cannot be negative.");
        if (value > max) return Fail(field, $"{label} is too large (max {max:N0}).");
        return this;
    }

    public Check Range(string field, string label, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max) Fail(field, $"{label} must be between {min:0.##} and {max:0.##}.");
        return this;
    }

    public Check NotFuture(string field, string label, DateOnly? value, DateOnly today)
    {
        if (value is { } d && d > today) Fail(field, $"{label} cannot be in the future.");
        return this;
    }

    public Check NotBefore(string field, string label, DateOnly? value, int minYear = 1900)
    {
        if (value is { } d && d.Year < minYear) Fail(field, $"{label} is not a valid date.");
        return this;
    }

    public Check Url(string field, string label, string? value, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        var v = value.Trim();
        if (v.Length > max) return Fail(field, $"{label} is too long (max {max} characters).");
        if (!(v.StartsWith('/') || Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)))
            Fail(field, $"{label} must be a web address starting with http:// or https://.");
        return this;
    }

    /// <summary>Null when valid; otherwise the 400 the controller should return.</summary>
    public IActionResult? Result() =>
        Ok ? null : new BadRequestObjectResult(new { message = string.Join(" ", _errors.Values), errors = _errors });

    [GeneratedRegex(@"^[0-9+()\-.\s]{7,20}$")] private static partial Regex PhoneRx();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRx();
    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")] private static partial Regex HexRx();
}
// JJQR - END
