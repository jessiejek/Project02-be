// JJQR - AI (Nov 20, 2026) - PV-7: the upload type is checked on the server from the file's own first bytes and its extension, not just from the content-type the browser claims
namespace ClinicApp.Infrastructure.Files;

public static class FileSignature
{
    private static readonly Dictionary<string, string[]> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = [".pdf"],
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"],
        ["image/webp"] = [".webp"],
        ["image/gif"] = [".gif"],
        ["application/msword"] = [".doc"],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
    };

    /// <summary>Null when the file looks like what it claims to be; otherwise a short reason for the 400.</summary>
    public static string? Problem(string fileName, string contentType, ReadOnlySpan<byte> head)
    {
        if (!Extensions.TryGetValue(contentType, out var exts)) return $"Content type '{contentType}' is not allowed.";
        var ext = Path.GetExtension(fileName ?? "");
        if (!exts.Contains(ext, StringComparer.OrdinalIgnoreCase)) return "The file name does not match its type.";
        return Matches(contentType, head) ? null : "The file's contents do not match its type.";
    }

    private static bool Matches(string contentType, ReadOnlySpan<byte> h) => contentType.ToLowerInvariant() switch
    {
        "application/pdf" => h.StartsWith("%PDF-"u8),
        "image/jpeg" => h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF,
        "image/png" => h.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/gif" => h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8),
        "image/webp" => h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h.Slice(8, 4).SequenceEqual("WEBP"u8),
        "application/msword" => h.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => h.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
        _ => false,
    };
}
// JJQR - END
