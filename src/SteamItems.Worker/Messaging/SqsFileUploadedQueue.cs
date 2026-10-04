using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;

namespace SteamItems.Worker.Messaging;

/// <summary>The <c>Queue</c> configuration section.</summary>
public sealed class SqsQueueOptions
{
    public const string SectionName = "Queue";

    [Required]
    public string QueueName { get; set; } = "";

    [Required]
    public string Region { get; set; } = "";

    /// <summary>Custom endpoint (LocalStack). Leave empty for real AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Static credentials (LocalStack uses <c>test</c>/<c>test</c>). Leave empty to use the default AWS credential chain.</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Long polling: how long one receive call waits for a message (SQS maximum is 20).</summary>
    [Range(0, 20)]
    public int WaitTimeSeconds { get; set; } = 20;

    [Range(1, 10)]
    public int MaxNumberOfMessages { get; set; } = 10;
}

/// <summary><see cref="IFileUploadedQueue"/> backed by SQS (or LocalStack). The only place that uses the AWS SDK.</summary>
public sealed class SqsFileUploadedQueue : IFileUploadedQueue, IDisposable
{
    private readonly AmazonSQSClient client;
    private readonly SqsQueueOptions settings;
    private string? queueUrl;

    public SqsFileUploadedQueue(IOptions<SqsQueueOptions> options)
    {
        settings = options.Value;

        var config = new AmazonSQSConfig
        {
            // Must outlast the long poll, or every empty receive would time out.
            Timeout = TimeSpan.FromSeconds(settings.WaitTimeSeconds + 10),
            MaxErrorRetry = 2,
        };
        if (string.IsNullOrEmpty(settings.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }
        else
        {
            config.ServiceURL = settings.ServiceUrl;
            config.AuthenticationRegion = settings.Region;
        }

        client = string.IsNullOrEmpty(settings.AccessKey)
            ? new AmazonSQSClient(config)
            : new AmazonSQSClient(new BasicAWSCredentials(settings.AccessKey, settings.SecretKey), config);
    }

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = await GetQueueUrlAsync(cancellationToken),
                WaitTimeSeconds = settings.WaitTimeSeconds,
                MaxNumberOfMessages = settings.MaxNumberOfMessages,
            }, cancellationToken);

            // SDK v4 leaves collections null when the response has none.
            return response.Messages?.Select(m => new QueueMessage(m.MessageId, m.ReceiptHandle, m.Body)).ToList() ?? [];
        }
        catch (Exception ex) when (IsQueueFailure(ex, cancellationToken))
        {
            throw new QueueException($"Could not receive from queue '{settings.QueueName}'.", ex);
        }
    }

    public async Task DeleteAsync(string receiptHandle, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteMessageAsync(await GetQueueUrlAsync(cancellationToken), receiptHandle, cancellationToken);
        }
        catch (Exception ex) when (IsQueueFailure(ex, cancellationToken))
        {
            throw new QueueException($"Could not delete a message from queue '{settings.QueueName}'.", ex);
        }
    }

    // Resolved on first use, not in the constructor, so the Worker starts even when the queue is down.
    private async Task<string> GetQueueUrlAsync(CancellationToken cancellationToken) =>
        queueUrl ??= (await client.GetQueueUrlAsync(settings.QueueName, cancellationToken)).QueueUrl;

    // Endpoint down, timeout, or an SQS error (missing queue, bad credentials). Caller cancellation is not a failure.
    private static bool IsQueueFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        AmazonServiceException or AmazonClientException => true,
        HttpRequestException or SocketException or IOException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    public void Dispose() => client.Dispose();
}
