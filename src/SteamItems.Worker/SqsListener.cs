using System.Threading.Channels;
using Polly;
using Polly.Registry;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Resilience;

namespace SteamItems.Worker;

/// <summary>
/// Long-polls the upload queue and hands each file event to the processors through the channel.
/// Messages are not deleted here (except ones with nothing to process); <see cref="FileProcessor"/> deletes them when done.
/// While the queue is unreachable, receiving is retried with backoff (<see cref="WorkerPipelines.QueueReceive"/>) until it works again.
/// </summary>
public sealed class SqsListener(
    IFileUploadedQueue queue,
    Channel<FileUploadedMessage> channel,
    ResiliencePipelineProvider<string> pipelines,
    ILogger<SqsListener> logger) : BackgroundService
{
    private const int MaxLoggedBodyLength = 500;

    // Longer than the 20 s long poll, shorter than HostOptions.ShutdownTimeout.
    private static readonly TimeSpan ShutdownReceiveLimit = TimeSpan.FromSeconds(25);

    private readonly ResiliencePipeline receivePipeline = pipelines.GetPipeline(WorkerPipelines.QueueReceive);
    private readonly ResiliencePipeline acknowledgePipeline = pipelines.GetPipeline(WorkerPipelines.QueueAcknowledge);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Listening for file uploads");

        // Cancels a receive only if it is still running well after shutdown started (SQS hanging, not just long polling).
        using var receiveCancellation = new CancellationTokenSource();
        await using var stopRegistration = stoppingToken.Register(() => receiveCancellation.CancelAfter(ShutdownReceiveLimit));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Only returns once a receive works (or throws on shutdown); each failure is logged by the pipeline.
                // The receive itself is not cancelled on shutdown: SQS keeps an abandoned long poll open and would hand it
                // messages (even ones just released) that nobody sees until the visibility timeout ends. So the poll runs
                // to its end (WaitTimeSeconds at most) and what it returns goes to the channel, where the processors release it.
                var messages = await receivePipeline.ExecuteAsync(
                    async _ => await queue.ReceiveAsync(receiveCancellation.Token), stoppingToken);

                foreach (var message in messages)
                {
                    await HandleAsync(message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown (Ctrl+C / SIGTERM).
        }
        finally
        {
            // No more writes: processors finish what is already in the channel, then their loops end.
            channel.Writer.TryComplete();
        }

        logger.LogInformation("Listener stopped");
    }

    private async Task HandleAsync(QueueMessage message)
    {
        switch (S3EventParser.Parse(message.Body))
        {
            case S3EventParseResult.Created created:
                // Unbounded and only completed by this loop, so the write always succeeds, also during shutdown.
                channel.Writer.TryWrite(new FileUploadedMessage(message.MessageId, message.ReceiptHandle, created.Events));
                break;

            case S3EventParseResult.Ignored ignored:
                logger.LogInformation("Ignoring message {MessageId}: {Reason}", message.MessageId, ignored.Reason);
                await DeleteAsync(message);
                break;

            case S3EventParseResult.Invalid invalid:
                // Left on the queue: it is redelivered and lands in the DLQ after maxReceiveCount, where it can be inspected.
                logger.LogWarning(
                    "Malformed message {MessageId}: {Reason} Body: {Body}",
                    message.MessageId, invalid.Reason, Truncate(message.Body));
                break;
        }
    }

    private async Task DeleteAsync(QueueMessage message)
    {
        try
        {
            // Not tied to the stopping token: the message was handled, finish removing it.
            await acknowledgePipeline.ExecuteAsync(
                async token => await queue.DeleteAsync(message.ReceiptHandle, token), CancellationToken.None);
        }
        catch (QueueException ex)
        {
            logger.LogWarning(ex, "Could not delete message {MessageId}; it will be delivered again", message.MessageId);
        }
    }

    private static string Truncate(string body) =>
        body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength] + "…";
}
