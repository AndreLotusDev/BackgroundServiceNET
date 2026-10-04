using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace SteamItems.Web.Storage;

/// <summary>The <c>FileStorage</c> configuration section.</summary>
public sealed class S3StorageOptions
{
    public const string SectionName = "FileStorage";

    [Required]
    public string BucketName { get; set; } = "";

    [Required]
    public string Region { get; set; } = "";

    /// <summary>Custom endpoint (LocalStack). Leave empty for real AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Static credentials (LocalStack uses <c>test</c>/<c>test</c>). Leave empty to use the default AWS credential chain.</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Per-request timeout, so a dead endpoint fails fast instead of hanging the request.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary><see cref="IFileStorage"/> backed by S3 (or LocalStack). The only place that uses the AWS SDK.</summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client client;
    private readonly string bucketName;
    private readonly ILogger<S3FileStorage> logger;

    public S3FileStorage(IOptions<S3StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        var settings = options.Value;
        bucketName = settings.BucketName;
        this.logger = logger;

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

    public async Task UploadAsync(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        foreach (var (name, value) in metadata)
        {
            request.Metadata.Add(name, value); // Sent as x-amz-meta-<name>.
        }

        try
        {
            await client.PutObjectAsync(request, cancellationToken);
        }
        catch (Exception ex) when (IsStorageFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Upload of {Key} to bucket {Bucket} failed", key, bucketName);
            throw new FileStorageException($"Could not upload '{key}' to bucket '{bucketName}'.", ex);
        }

        logger.LogInformation("Uploaded {Key} to bucket {Bucket}", key, bucketName);
    }

    // Endpoint down, timeout, or an S3 error (missing bucket, bad credentials). Caller cancellation is not a failure.
    private static bool IsStorageFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        AmazonServiceException or AmazonClientException => true,
        HttpRequestException or SocketException or IOException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    public void Dispose() => client.Dispose();
}
