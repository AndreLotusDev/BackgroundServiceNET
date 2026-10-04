using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using SteamItems.Worker.Import;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Resilience;

namespace SteamItems.Worker;

/// <summary>The <c>Worker</c> configuration section.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How many loops read the channel at the same time.</summary>
    [Range(1, 64)]
    public int ProcessorCount { get; set; } = 1;

    /// <summary>
    /// While a message is processed, its visibility timeout is pushed forward this often, so SQS does not hand it out
    /// again (and count a receive towards the DLQ) in the middle of a large file.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:10:00")]
    public TimeSpan VisibilityHeartbeat { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long each heartbeat hides the message for. If the Worker dies, the message comes back after at most this.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "12:00:00")]
    public TimeSpan VisibilityExtension { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Reads file events from the channel and imports each file; the SQS message is deleted only after every file in it is imported.
/// A failed message is logged and left on the queue (redelivered, then moved to the DLQ); the loop goes on with the next one.
/// While a message is processed a heartbeat keeps it hidden on the queue (<see cref="WorkerOptions.VisibilityHeartbeat"/>).
/// On shutdown the current file stops after its current row, and every message not finished is released back to the queue.
/// </summary>
public sealed class FileProcessor(
    Channel<FileUploadedMessage> channel,
    IFileUploadedQueue queue,
    IServiceScopeFactory scopeFactory,
    ResiliencePipelineProvider<string> pipelines,
    IOptions<WorkerOptions> options,
    ILogger<FileProcessor> logger) : BackgroundService
{
    private readonly ResiliencePipeline acknowledgePipeline = pipelines.GetPipeline(WorkerPipelines.QueueAcknowledge);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var count = options.Value.ProcessorCount;
        logger.LogInformation("Starting {Count} file processor(s)", count);

        await Task.WhenAll(Enumerable.Range(1, count).Select(id => RunAsync(id, stoppingToken)));

        logger.LogInformation("File processors stopped");
    }

    private async Task RunAsync(int processorId, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(processorId, message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "Processor {ProcessorId} stopped in the middle of message {MessageId}; it goes back to the queue",
                        processorId, message.MessageId);
                    await ReleaseAsync(message);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Processing message {MessageId} failed; it stays on the queue and will be retried", message.MessageId);
                    continue;
                }

                await DeleteAsync(message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown while waiting for the next message.
        }

        // The listener stopped first and completed the channel; whatever is left was received but never started.
        while (channel.Reader.TryRead(out var pending))
        {
            await ReleaseAsync(pending);
        }
    }

    private async Task ProcessAsync(int processorId, FileUploadedMessage message, CancellationToken stoppingToken)
    {
        using var heartbeatCancellation = new CancellationTokenSource();
        var heartbeat = KeepHiddenAsync(message, heartbeatCancellation.Token);
        try
        {
            foreach (var file in message.Events)
            {
                logger.LogInformation(
                    "Processor {ProcessorId} got file {Key} in bucket {Bucket} (message {MessageId})",
                    processorId, file.Key, file.Bucket, message.MessageId);

                // One scope (and DbContext) per file.
                await using var scope = scopeFactory.CreateAsyncScope();
                var importer = scope.ServiceProvider.GetRequiredService<IFileImporter>();
                await importer.ImportAsync(file, stoppingToken);
            }
        }
        finally
        {
            await heartbeatCancellation.CancelAsync();
            await heartbeat;
        }
    }

    private async Task KeepHiddenAsync(FileUploadedMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.VisibilityHeartbeat);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await queue.ChangeVisibilityAsync(message.ReceiptHandle, settings.VisibilityExtension, cancellationToken);
                }
                catch (QueueException ex)
                {
                    // Keep going: the next beat may work. If none do, the message is delivered again and the file resumes.
                    logger.LogWarning(ex, "Could not extend the visibility of message {MessageId}", message.MessageId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The message is done (or abandoned on shutdown).
        }
    }

    private async Task DeleteAsync(FileUploadedMessage message)
    {
        try
        {
            // Not tied to the stopping token: the file was processed, finish removing the message.
            await acknowledgePipeline.ExecuteAsync(
                async token => await queue.DeleteAsync(message.ReceiptHandle, token), CancellationToken.None);
        }
        catch (QueueException ex)
        {
            logger.LogWarning(ex, "Could not delete message {MessageId}; it will be delivered again", message.MessageId);
        }
    }

    // Without this the message would stay invisible until its visibility timeout runs out.
    private async Task ReleaseAsync(FileUploadedMessage message)
    {
        try
        {
            await acknowledgePipeline.ExecuteAsync(
                async token => await queue.ChangeVisibilityAsync(message.ReceiptHandle, TimeSpan.Zero, token), CancellationToken.None);
            logger.LogInformation("Released message {MessageId} back to the queue", message.MessageId);
        }
        catch (QueueException ex)
        {
            logger.LogWarning(
                ex, "Could not release message {MessageId}; it comes back after its visibility timeout", message.MessageId);
        }
    }
}
