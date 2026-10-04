namespace SteamItems.Worker.Data;

/// <summary>One version of an uploaded object (bucket + key + ETag). Unique, so a redelivered event is not processed twice.</summary>
public class ProcessedFile
{
    public int Id { get; set; }
    public required string Bucket { get; set; }
    public required string Key { get; set; }
    public required string ETag { get; set; }

    /// <summary>From the workbook properties; null when the file does not carry them.</summary>
    public Guid? ExportId { get; set; }

    public string? UserId { get; set; }

    public FileStatus Status { get; set; }

    /// <summary>Why the whole file was rejected (<see cref="FileStatus.Failed"/>).</summary>
    public string? Error { get; set; }

    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<ProcessedItem> Items { get; } = [];
}

public enum FileStatus
{
    /// <summary>Rows are being stored. A file left in this state (crash, shutdown) resumes when the event comes back.</summary>
    Processing,

    /// <summary>Every row was stored, as <see cref="ItemStatus.Processed"/> or <see cref="ItemStatus.Failed"/>.</summary>
    Completed,

    /// <summary>The file could not be read at all (not a workbook, wrong layout). No rows stored.</summary>
    Failed,
}
