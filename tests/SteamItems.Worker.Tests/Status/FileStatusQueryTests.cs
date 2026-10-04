using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Worker.Data;
using SteamItems.Worker.Status;

namespace SteamItems.Worker.Tests.Status;

/// <summary>The status endpoint's query against the real schema (the migrations, in memory).</summary>
public sealed class FileStatusQueryTests : IDisposable
{
    private const string Bucket = "steam-items-uploads";
    private static readonly DateTimeOffset Started = new(2026, 10, 4, 13, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public FileStatusQueryTests()
    {
        connection.Open();
        using var db = CreateDb();
        db.Database.Migrate();
    }

    [Fact]
    public async Task A_completed_file_reports_its_stored_counts()
    {
        await AddFileAsync("exports/a.xlsx", FileStatus.Completed, processed: 3, failed: 1,
            completedAt: Started.AddMinutes(1), items: [ItemStatus.Processed]);

        var status = Assert.Single(await QueryAsync("exports/a.xlsx"));

        Assert.Equal("exports/a.xlsx", status.Key);
        Assert.Equal(FileProcessingStatus.Completed, status.Status);
        // The stored totals, not a recount of the items.
        Assert.Equal((3, 1), (status.ProcessedCount, status.FailedCount));
        Assert.Null(status.Error);
        Assert.Equal(Started, status.StartedAt);
        Assert.Equal(Started.AddMinutes(1), status.CompletedAt);
    }

    [Fact]
    public async Task A_file_still_processing_reports_the_rows_stored_so_far()
    {
        await AddFileAsync("exports/a.xlsx", FileStatus.Processing,
            items: [ItemStatus.Processed, ItemStatus.Processed, ItemStatus.Failed]);
        await AddFileAsync("exports/b.xlsx", FileStatus.Processing, items: [ItemStatus.Processed]);

        var statuses = (await QueryAsync("exports/a.xlsx", "exports/b.xlsx")).ToDictionary(s => s.Key);

        Assert.Equal(FileProcessingStatus.Processing, statuses["exports/a.xlsx"].Status);
        Assert.Equal((2, 1), (statuses["exports/a.xlsx"].ProcessedCount, statuses["exports/a.xlsx"].FailedCount));
        Assert.Equal((1, 0), (statuses["exports/b.xlsx"].ProcessedCount, statuses["exports/b.xlsx"].FailedCount));
        Assert.Null(statuses["exports/a.xlsx"].CompletedAt);
    }

    [Fact]
    public async Task A_rejected_file_reports_failed_with_the_reason()
    {
        await AddFileAsync("exports/a.xlsx", FileStatus.Failed, error: "Not an .xlsx workbook.", completedAt: Started);

        var status = Assert.Single(await QueryAsync("exports/a.xlsx"));

        Assert.Equal(FileProcessingStatus.Failed, status.Status);
        Assert.Equal("Not an .xlsx workbook.", status.Error);
    }

    [Fact]
    public async Task Unknown_keys_are_left_out()
    {
        await AddFileAsync("exports/a.xlsx", FileStatus.Completed, processed: 1, completedAt: Started);

        var statuses = await QueryAsync("exports/a.xlsx", "exports/not-seen-yet.xlsx");

        Assert.Equal("exports/a.xlsx", Assert.Single(statuses).Key);
    }

    [Fact]
    public async Task The_same_key_uploaded_again_reports_the_latest_file()
    {
        await AddFileAsync("exports/a.xlsx", FileStatus.Completed, eTag: "\"v1\"", processed: 5, completedAt: Started);
        await AddFileAsync("exports/a.xlsx", FileStatus.Processing, eTag: "\"v2\"", started: Started.AddHours(1),
            items: [ItemStatus.Processed]);

        var status = Assert.Single(await QueryAsync("exports/a.xlsx"));

        Assert.Equal(FileProcessingStatus.Processing, status.Status);
        Assert.Equal(1, status.ProcessedCount);
    }

    private async Task<IReadOnlyList<FileStatusResponse>> QueryAsync(params string[] keys)
    {
        await using var db = CreateDb();
        return await new FileStatusQuery(db).GetAsync(keys, CancellationToken.None);
    }

    private async Task AddFileAsync(
        string key,
        FileStatus status,
        string eTag = "\"etag\"",
        int processed = 0,
        int failed = 0,
        string? error = null,
        DateTimeOffset? started = null,
        DateTimeOffset? completedAt = null,
        ItemStatus[]? items = null)
    {
        await using var db = CreateDb();
        var file = new ProcessedFile
        {
            Bucket = Bucket,
            Key = key,
            ETag = eTag,
            Status = status,
            ProcessedCount = processed,
            FailedCount = failed,
            Error = error,
            StartedAt = started ?? Started,
            CompletedAt = completedAt,
        };
        file.Items.AddRange((items ?? []).Select((itemStatus, i) => new ProcessedItem
        {
            RowNumber = i + 2,
            Status = itemStatus,
            ProcessedAt = Started,
        }));
        db.ProcessedFiles.Add(file);
        await db.SaveChangesAsync();
    }

    private WorkerDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<WorkerDbContext>().UseSqlite(connection).Options);

    public void Dispose() => connection.Dispose();
}
