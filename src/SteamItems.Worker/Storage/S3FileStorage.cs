using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace SteamItems.Worker.Storage;

/// <summary>The <c>FileStorage</c> configuration section. The bucket comes from each S3 event, so it is not configured.</summary>
public sealed class S3StorageOptions
{
    public const string SectionName = "FileStorage";

    [Required]
    public string Region { get; set; } = "";

    /// <summary>Custom endpoint (LocalStack). Leave empty for real AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Static credentials (LocalStack uses <c>test</c>/<c>test</c>). Leave empty to use the default AWS credential chain.</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Per-request timeout, so a dead endpoint fails fast instead of holding a processor.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary><see cref="IFileStorage"/> backed by S3 (or LocalStack). The only place that uses the S3 SDK.</summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client client;

    public S3FileStorage(IOptions<S3StorageOptions> options)
    {
        var settings = options.Value;

        var config = new AmazonS3Config
        {
            Timeout = settings.Timeout,
            MaxErrorRetry = 2,
        };
        if (string.IsNullOrEmpty(settings.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }
        else
        {
            // LocalStack serves every bucket from one host, so use path-style URLs (http://host/bucket/key).
            config.ServiceURL = settings.ServiceUrl;
            config.AuthenticationRegion = settings.Region;
            config.ForcePathStyle = true;
        }

        client = string.IsNullOrEmpty(settings.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
    }

    public async Task<StoredFile> DownloadAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetObjectAsync(bucket, key, cancellationToken);

            // ClosedXML needs a seekable stream; the response stream is not.
            var content = new MemoryStream();
            await response.ResponseStream.CopyToAsync(content, cancellationToken);
            content.Position = 0;

            return new StoredFile(response.ETag.Trim('"'), content);
        }
        catch (Exception ex) when (IsStorageFailure(ex, cancellationToken))
        {
            throw new FileStorageException($"Could not download '{key}' from bucket '{bucket}'.", ex, IsTransient(ex));
        }
    }

    // A 4xx answer (missing object, access denied) will be the same next time; 408 and 429 are worth another try.
    private static bool IsTransient(Exception ex) => ex is not AmazonServiceException { StatusCode: var status }
        || (int)status is >= 500 or 0 or 408 or 429;

    // Endpoint down, timeout, or an S3 error (missing object, bad credentials). Caller cancellation is not a failure.
    private static bool IsStorageFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        AmazonServiceException or AmazonClientException => true,
        HttpRequestException or SocketException or IOException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    public void Dispose() => client.Dispose();
}
