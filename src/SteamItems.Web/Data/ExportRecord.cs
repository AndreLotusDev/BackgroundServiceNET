using SteamItems.Contracts.Status;

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

    /// <summary>Fila badge classes for the status (also sent to /Exports over SignalR).</summary>
    public static string BadgeClass(this ExportStatus status) => StatusBadge.For(status switch
    {
        ExportStatus.Pending => "secondary",
        ExportStatus.Processing => "primary",
        ExportStatus.Completed => "success",
        ExportStatus.CompletedWithErrors => "warning",
        ExportStatus.Failed => "danger",
        _ => "secondary",
    });
}

public static class FileItemStatusExtensions
{
    /// <summary>Fila badge classes for a row status, same colours as the matching <see cref="ExportStatus"/>.</summary>
    public static string BadgeClass(this FileItemStatus status) =>
        StatusBadge.For(status == FileItemStatus.Failed ? "danger" : "success");
}

/// <summary>Fila's soft badge (fila_samples/fila/orders.html): coloured text on a 10% background of the same colour.</summary>
public static class StatusBadge
{
    public static string For(string color) =>
        $"default-badge d-inline-block fs-14 fw-normal text-{color} bg-{color} bg-opacity-10";
}
