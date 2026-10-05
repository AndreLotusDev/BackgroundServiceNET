using System.Text.Json.Serialization;

namespace SteamItems.Contracts.Status;

/// <summary>The Worker's rows endpoint, which Web calls to show what was read from one uploaded file.</summary>
public static class FileItemsApi
{
    /// <summary>
    /// <c>GET {Path}?key=…&amp;status=…&amp;page=…&amp;pageSize=…</c> → <see cref="FileItemsPage"/>, rows in Excel row order.
    /// 404 when the Worker has no file for the key.
    /// </summary>
    public const string Path = "/api/files/items";

    public const string KeyParameter = "key";

    /// <summary>Optional <see cref="FileItemStatus"/>; all rows when left out.</summary>
    public const string StatusParameter = "status";

    /// <summary>1-based.</summary>
    public const string PageParameter = "page";

    public const string PageSizeParameter = "pageSize";

    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;
}

/// <summary>One page of the rows the Worker stored for the latest file uploaded under <see cref="Key"/>.</summary>
/// <param name="FileStatus">Rows are only complete once the file is <see cref="FileProcessingStatus.Completed"/>.</param>
/// <param name="TotalCount">Rows that match the status filter, over every page.</param>
/// <param name="ProcessedCount">Rows stored as processed in the whole file, whatever the filter.</param>
/// <param name="FailedCount">Rows stored as failed in the whole file, whatever the filter.</param>
public sealed record FileItemsPage(
    string Key,
    FileProcessingStatus FileStatus,
    FileItemStatus? Filter,
    int Page,
    int PageSize,
    int TotalCount,
    int ProcessedCount,
    int FailedCount,
    IReadOnlyList<FileItemResponse> Items)
{
    public int PageCount => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
}

/// <summary>One data row of the file.</summary>
/// <param name="RowNumber">Excel row number (the header is row 1).</param>
/// <param name="AppId">Null on a failed row when the cell did not parse; same for the other values.</param>
/// <param name="Error">Why the row failed, with the value that did not parse.</param>
/// <param name="Raw">The cells as text, only on failed rows.</param>
public sealed record FileItemResponse(
    int RowNumber,
    FileItemStatus Status,
    int? AppId,
    string? Name,
    decimal? Price,
    DateOnly? ReleaseDate,
    string? Error,
    FileItemRawCells? Raw);

/// <summary>The cells of a failed row as they were in the file.</summary>
public sealed record FileItemRawCells(string? AppId, string? Name, string? Price, string? ReleaseDate);

[JsonConverter(typeof(JsonStringEnumConverter<FileItemStatus>))]
public enum FileItemStatus
{
    Processed,
    Failed,
}
