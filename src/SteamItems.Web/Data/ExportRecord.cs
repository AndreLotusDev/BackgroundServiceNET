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
}

/// <summary>Where the export is in the pipeline; task 11 adds the Worker's outcome.</summary>
public enum ExportStatus
{
    Uploaded,
}
