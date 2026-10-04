namespace SteamItems.Worker.Messaging;

/// <summary>A file was created in the bucket. Built from one S3 <c>ObjectCreated:*</c> record.</summary>
/// <param name="Key">Object key, already URL-decoded.</param>
public sealed record FileUploadedEvent(string Bucket, string Key);

/// <summary>
/// One SQS message on its way to a processor. The message is deleted from the queue only after
/// processing succeeds, so the receipt handle travels with the events.
/// </summary>
public sealed record FileUploadedMessage(string MessageId, string ReceiptHandle, IReadOnlyList<FileUploadedEvent> Events);
