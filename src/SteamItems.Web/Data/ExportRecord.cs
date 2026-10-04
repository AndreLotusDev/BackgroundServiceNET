namespace SteamItems.Web.Data;

/// <summary>An Excel export uploaded to storage. <see cref="Id"/> is the export id written in the workbook.</summary>
public class ExportRecord
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string ObjectKey { get; set; }
    public required string FileName { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ExportStatus Status { get; set; }

    // The Worker's outcome, copied from its status endpoint by ExportStatusPoller.

    /// <summary>Rows the Worker stored as processed (so far, while <see cref="ExportStatus.Processing"/>).</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Rows the Worker stored as failed (so far, while <see cref="ExportStatus.Processing"/>).</summary>
    public int FailedCount { get; set; }

    /// <summary>Why the Worker rejected the whole file (<see cref="ExportStatus.Failed"/>).</summary>
    public string? Error { get; set; }

    /// <summary>When the Worker finished the file.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Where the export is in the pipeline.</summary>
public enum ExportStatus
{
    /// <summary>Uploaded; the Worker has not picked it up yet (or is down).</summary>
    Pending,

    /// <summary>The Worker is storing its rows.</summary>
    Processing,

    /// <summary>Every row was processed.</summary>
    Completed,

    /// <summary>Every row was stored, but some failed (see <see cref="ExportRecord.FailedCount"/>).</summary>
    CompletedWithErrors,

    /// <summary>The Worker could not read the file at all (see <see cref="ExportRecord.Error"/>).</summary>
    Failed,
}

public static class ExportStatusExtensions
{
    /// <summary>The Worker is done with it; no more polling.</summary>
    public static bool IsFinal(this ExportStatus status) =>
        status is ExportStatus.Completed or ExportStatus.CompletedWithErrors or ExportStatus.Failed;

    public static string DisplayText(this ExportStatus status) => status switch
    {
        ExportStatus.CompletedWithErrors => "Completed with errors",
        _ => status.ToString(),
    };

    /// <summary>Bootstrap badge colour for the status.</summary>
    public static string BadgeClass(this ExportStatus status) => status switch
    {
        ExportStatus.Pending => "text-bg-secondary",
        ExportStatus.Processing => "text-bg-info",
        ExportStatus.Completed => "text-bg-success",
        ExportStatus.CompletedWithErrors => "text-bg-warning",
        ExportStatus.Failed => "text-bg-danger",
        _ => "text-bg-light",
    };
}
