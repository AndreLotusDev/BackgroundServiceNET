namespace SteamItems.Worker.Data;

/// <summary>One data row of a file. Unique on (<see cref="FileId"/>, <see cref="RowNumber"/>).</summary>
public class ProcessedItem
{
    public int Id { get; set; }
    public int FileId { get; set; }

    /// <summary>Excel row number (1-based, the header is row 1).</summary>
    public int RowNumber { get; set; }

    // Null only on failed rows, for the values that did not parse.
    public int? AppId { get; set; }
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public DateOnly? ReleaseDate { get; set; }

    public ItemStatus Status { get; set; }
    public string? Error { get; set; }

    /// <summary>The cells as text, kept only for failed rows so the bad value can be seen.</summary>
    public string? RawAppId { get; set; }

    public string? RawName { get; set; }
    public string? RawPrice { get; set; }
    public string? RawReleaseDate { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

public enum ItemStatus
{
    Processed,
    Failed,
}
