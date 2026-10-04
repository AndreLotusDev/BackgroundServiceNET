namespace SteamItems.Worker.Storage;

/// <summary>Object storage the uploaded files are read from. Implementations keep their SDK types to themselves.</summary>
public interface IFileStorage
{
    /// <summary>Downloads the object into memory, so the caller gets a seekable stream.</summary>
    /// <exception cref="FileStorageException">The storage could not be reached, the object is missing or the request was refused.</exception>
    Task<StoredFile> DownloadAsync(string bucket, string key, CancellationToken cancellationToken);
}

/// <param name="ETag">Identifies this version of the object; changes when the same key is uploaded again. Without quotes.</param>
/// <param name="Content">Seekable, positioned at the start. The caller disposes it.</param>
public sealed record StoredFile(string ETag, Stream Content);

/// <summary>The storage is down, misconfigured or rejected the request.</summary>
public sealed class FileStorageException(string message, Exception innerException)
    : Exception(message, innerException);
