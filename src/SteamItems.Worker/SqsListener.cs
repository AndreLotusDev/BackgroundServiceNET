using System.Threading.Channels;
using SteamItems.Worker.Messaging;

namespace SteamItems.Worker;

/// <summary>
/// Long-polls the upload queue and hands each file event to the processors through the channel.
/// Messages are not deleted here (except ones with nothing to process); <see cref="FileProcessor"/> deletes them when done.
/// </summary>
public sealed class SqsListener(
    IFileUploadedQueue queue,
    Channel<FileUploadedMessage> channel,
    ILogger<SqsListener> logger) : BackgroundService
{
    private static readonly TimeSpan ReceiveRetryDelay = TimeSpan.FromSeconds(5);
    private const int MaxLoggedBodyLength = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Listening for file uploads");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<QueueMessage> messages;
                try
                {
                    messages = await queue.ReceiveAsync(stoppingToken);
                }
                catch (QueueException ex)
                {
                    logger.LogWarning(ex, "Could not receive messages, retrying in {Delay}", ReceiveRetryDelay);
                    await Task.Delay(ReceiveRetryDelay, stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    await HandleAsync(message, stoppingToken);
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

    private async Task HandleAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        switch (S3EventParser.Parse(message.Body))
        {
            case S3EventParseResult.Created created:
                await channel.Writer.WriteAsync(
                    new FileUploadedMessage(message.MessageId, message.ReceiptHandle, created.Events), stoppingToken);
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
            await queue.DeleteAsync(message.ReceiptHandle, CancellationToken.None);
        }
        catch (QueueException ex)
        {
            logger.LogWarning(ex, "Could not delete message {MessageId}; it will be delivered again", message.MessageId);
        }
    }

    private static string Truncate(string body) =>
        body.Length <= MaxLoggedBodyLength ? body : body[..MaxLoggedBodyLength] + "…";
}
