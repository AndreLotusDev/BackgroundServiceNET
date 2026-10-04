namespace SteamItems.Worker.Messaging;

/// <summary>The queue that receives S3 upload notifications. Implementations keep their SDK types to themselves.</summary>
public interface IFileUploadedQueue
{
    /// <summary>Long-polls for the next batch. Returns an empty list when the wait time passes with no messages.</summary>
    /// <exception cref="QueueException">The queue could not be reached or refused the request.</exception>
    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Removes a handled message so it is not delivered again.</summary>
    /// <exception cref="QueueException">The queue could not be reached or refused the request.</exception>
    Task DeleteAsync(string receiptHandle, CancellationToken cancellationToken);
}

/// <param name="ReceiptHandle">Needed to delete this delivery of the message.</param>
public sealed record QueueMessage(string MessageId, string ReceiptHandle, string Body);

/// <summary>The queue is down, misconfigured or rejected the request.</summary>
public sealed class QueueException(string message, Exception innerException)
    : Exception(message, innerException);
