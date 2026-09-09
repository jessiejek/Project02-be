using System.Text.RegularExpressions;

namespace ClinicApp.Infrastructure.Files;

public class FileStorageOptions
{
    /// <summary>Absolute path to the App_Data/uploads root. Defaults to {ContentRoot}/App_Data/uploads.</summary>
    public string RootPath { get; set; } = "";

    /// <summary>URL prefix the files are served under (mounted via UseStaticFiles in Program.cs).</summary>
    public string PublicPathPrefix { get; set; } = "/uploads";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB, per contract §8

    // Matches src/lib/patientUploads.ts allow-list.
    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" // docx
    };
}

/// <summary>Replaces Supabase Storage buckets (patient-documents / patient-lab-results) with local disk,
/// per DOTNET_BACKEND_PLAN.md §7.</summary>
public class LocalFileStorageService(FileStorageOptions options) : IFileStorageService
{
    public async Task<string> SaveAsync(Guid patientId, Guid bookingId, string fileName, Stream content, string contentType, CancellationToken ct = default)
    {
        if (!FileStorageOptions.AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException($"Content type '{contentType}' is not allowed.");
        }

        if (content.Length > options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File exceeds the {options.MaxFileSizeBytes / (1024 * 1024)} MB limit.");
        }

        var sanitized = Sanitize(fileName);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var relativeDir = Path.Combine(patientId.ToString(), bookingId.ToString());
        var relativeFile = Path.Combine(relativeDir, $"{timestamp}-{sanitized}");

        var absoluteDir = Path.Combine(options.RootPath, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var absoluteFile = Path.Combine(options.RootPath, relativeFile);
        await using (var fileStream = File.Create(absoluteFile))
        {
            content.Position = 0;
            await content.CopyToAsync(fileStream, ct);
        }

        // Normalize to forward slashes for the public URL regardless of OS.
        var urlPath = relativeFile.Replace(Path.DirectorySeparatorChar, '/');
        return $"{options.PublicPathPrefix}/{urlPath}";
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var trimmed = relativePath.TrimStart('/');
        if (trimmed.StartsWith(options.PublicPathPrefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[options.PublicPathPrefix.TrimStart('/').Length..].TrimStart('/');
        }

        var absolute = Path.Combine(options.RootPath, trimmed.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolute))
        {
            File.Delete(absolute);
        }

        return Task.CompletedTask;
    }

    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return Regex.Replace(name, @"[^a-zA-Z0-9._-]", "_");
    }
}
