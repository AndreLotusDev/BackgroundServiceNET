namespace SteamItems.Web.Storage;

/// <summary>Object storage for generated files. Implementations keep their SDK types to themselves.</summary>
public interface IFileStorage
{
    /// <summary>Stores <paramref name="content"/> under <paramref name="key"/>, replacing any existing object.</summary>
    /// <param name="metadata">Extra name/value pairs stored with the object.</param>
    /// <exception cref="FileStorageException">The storage could not be reached or refused the upload.</exception>
    Task UploadAsync(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);
}

/// <summary>The storage is down, misconfigured or rejected the request.</summary>
public sealed class FileStorageException(string message, Exception innerException)
    : Exception(message, innerException);
