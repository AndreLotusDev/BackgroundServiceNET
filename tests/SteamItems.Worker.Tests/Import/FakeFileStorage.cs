using System.Collections.Concurrent;
using System.Security.Cryptography;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Tests.Import;

/// <summary>In-memory bucket. The ETag is the MD5 of the content, like S3 for a single-part upload.</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<(string Bucket, string Key), byte[]> objects = new();

    public bool IsDown { get; set; }

    public int Downloads { get; private set; }

    /// <summary>Every call, including the failed ones.</summary>
    public int Attempts { get; private set; }

    /// <summary>The next calls that fail as if the storage were down, before the call works again.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <returns>The ETag of the stored content.</returns>
    public string Put(string bucket, string key, byte[] content)
    {
        objects[(bucket, key)] = content;
        return ETagOf(content);
    }

    public static string ETagOf(byte[] content) => Convert.ToHexStringLower(MD5.HashData(content));

    public Task<StoredFile> DownloadAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        Attempts++;
        if (IsDown || FailuresBeforeSuccess-- > 0)
        {
            throw new FileStorageException($"Could not download '{key}' from bucket '{bucket}'.", new IOException("Connection refused"));
        }

        if (!objects.TryGetValue((bucket, key), out var content))
        {
            throw new FileStorageException($"Could not download '{key}' from bucket '{bucket}'.", new FileNotFoundException(key), isTransient: false);
        }

        Downloads++;
        return Task.FromResult(new StoredFile(ETagOf(content), new MemoryStream(content)));
    }
}
