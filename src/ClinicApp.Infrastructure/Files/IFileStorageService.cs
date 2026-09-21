namespace ClinicApp.Infrastructure.Files;

public interface IFileStorageService
{
    /// <summary>Saves the file under App_Data/uploads/{patientId}/{bookingId}/{timestamp}-{sanitizedFileName}
    /// (contract §8 path pattern) and returns the URL to store in file_url.</summary>
    Task<string> SaveAsync(Guid patientId, Guid bookingId, string fileName, Stream content, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Maps a stored `file_url` (`/uploads/{patient}/{booking}/...`) to the file on
    /// disk, or null when it doesn't point at an existing file *inside* the storage root
    /// (path traversal, legacy absolute URLs, deleted file). Callers must have already
    /// authorized the request — this does no access check.</summary>
    string? ResolvePath(string storedUrl);
}
