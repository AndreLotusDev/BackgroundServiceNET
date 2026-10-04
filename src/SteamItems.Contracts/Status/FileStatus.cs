using System.Text.Json.Serialization;

namespace SteamItems.Contracts.Status;

/// <summary>The Worker's status endpoint, which Web polls for the outcome of its uploads.</summary>
public static class FileStatusApi
{
    /// <summary><c>GET {Path}?key=…&amp;key=…</c> → <see cref="FileStatusResponse"/>[].</summary>
    public const string Path = "/api/files/status";

    public const string KeyParameter = "key";

    /// <summary>Most keys accepted in one request, so the query string stays short.</summary>
    public const int MaxKeys = 50;
}

/// <summary>
/// What the Worker knows about one uploaded object. Keys the Worker has not seen yet are left out of the response.
/// </summary>
/// <param name="Key">Object key, as sent in the request.</param>
/// <param name="ProcessedCount">Rows stored as processed; while <see cref="FileProcessingStatus.Processing"/>, the rows so far.</param>
/// <param name="FailedCount">Rows stored as failed; while <see cref="FileProcessingStatus.Processing"/>, the rows so far.</param>
/// <param name="Error">Why the whole file was rejected (<see cref="FileProcessingStatus.Failed"/>).</param>
public sealed record FileStatusResponse(
    string Key,
    FileProcessingStatus Status,
    int ProcessedCount,
    int FailedCount,
    string? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

[JsonConverter(typeof(JsonStringEnumConverter<FileProcessingStatus>))]
public enum FileProcessingStatus
{
    /// <summary>Rows are being stored (or the import was interrupted and waits for the message to come back).</summary>
    Processing,

    /// <summary>Every row was stored, as processed or failed.</summary>
    Completed,

    /// <summary>The file could not be read at all. No rows stored.</summary>
    Failed,
}
