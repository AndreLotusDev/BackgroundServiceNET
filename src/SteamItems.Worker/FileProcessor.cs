using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SteamItems.Worker.Import;
using SteamItems.Worker.Messaging;

namespace SteamItems.Worker;

/// <summary>The <c>Worker</c> configuration section.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>How many loops read the channel at the same time.</summary>
    [Range(1, 64)]
    public int ProcessorCount { get; set; } = 1;
}

/// <summary>
/// Reads file events from the channel and imports each file; the SQS message is deleted only after every file in it is imported.
/// </summary>
public sealed class FileProcessor(
    Channel<FileUploadedMessage> channel,
    IFileUploadedQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    ILogger<FileProcessor> logger) : BackgroundService
{
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
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Processing message {MessageId} failed; it stays on the queue and will be retried", message.MessageId);
                    continue;
                }

                await DeleteAsync(message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown. Messages still in the channel are not deleted, so SQS delivers them again.
        }
    }

    private async Task ProcessAsync(int processorId, FileUploadedMessage message, CancellationToken stoppingToken)
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

    private async Task DeleteAsync(FileUploadedMessage message)
    {
        try
        {
            // Not tied to the stopping token: the file was processed, finish removing the message.
            await queue.DeleteAsync(message.ReceiptHandle, CancellationToken.None);
        }
        catch (QueueException ex)
        {
            logger.LogWarning(ex, "Could not delete message {MessageId}; it will be delivered again", message.MessageId);
        }
    }
}
