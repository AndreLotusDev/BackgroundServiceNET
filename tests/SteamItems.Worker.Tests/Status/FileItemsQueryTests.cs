using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Worker.Data;
using SteamItems.Worker.Status;

namespace SteamItems.Worker.Tests.Status;

/// <summary>The rows endpoint's query against the real schema (the migrations, in memory).</summary>
public sealed class FileItemsQueryTests : IDisposable
{
    private const string Key = "exports/alice/a.xlsx";
    private static readonly DateTimeOffset Started = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public FileItemsQueryTests()
    {
        connection.Open();
        using var db = CreateDb();
        db.Database.Migrate();
    }

    [Fact]
    public async Task Returns_the_stored_values_in_row_order()
    {
        await AddFileAsync(Key, FileStatus.Completed, rows: [Processed(3, 30), Processed(2, 20)]);

        var page = await QueryAsync(Key);

        Assert.NotNull(page);
        Assert.Equal(FileProcessingStatus.Completed, page.FileStatus);
        Assert.Equal([2, 3], page.Items.Select(i => i.RowNumber));
        var row = page.Items[0];
        Assert.Equal(
            new FileItemResponse(2, FileItemStatus.Processed, 20, "Game 20", 9.99m, new DateOnly(2020, 1, 2), null, null),
            row);
        Assert.Equal((2, 0, 2), (page.TotalCount, page.FailedCount, page.ProcessedCount));
    }

    [Fact]
    public async Task A_failed_row_carries_the_error_and_the_raw_cells()
    {
        await AddFileAsync(Key, FileStatus.Completed, rows: [Processed(2, 20), Failed(3, "free")]);

        var page = await QueryAsync(Key);

        var failed = Assert.Single(page!.Items, i => i.Status == FileItemStatus.Failed);
        Assert.Equal(3, failed.RowNumber);
        Assert.Null(failed.Price);
        Assert.Equal("Price must be a number zero or greater, found \"free\".", failed.Error);
        Assert.Equal(new FileItemRawCells("30", "Game 30", "free", "2020-01-02"), failed.Raw);
        Assert.Null(page.Items.Single(i => i.Status == FileItemStatus.Processed).Raw);
    }

    [Fact]
    public async Task The_failed_filter_keeps_only_failed_rows_and_counts_them()
    {
        await AddFileAsync(Key, FileStatus.Completed,
            rows: [Processed(2, 20), Failed(3, "free"), Processed(4, 40), Failed(5, "-")]);

        var page = await QueryAsync(Key, FileItemStatus.Failed);

        Assert.Equal(FileItemStatus.Failed, page!.Filter);
        Assert.Equal([3, 5], page.Items.Select(i => i.RowNumber));
        Assert.Equal(2, page.TotalCount);
        // The file's totals stay the same whatever the filter.
        Assert.Equal((2, 2), (page.ProcessedCount, page.FailedCount));
    }

    [Fact]
    public async Task Pages_are_cut_in_row_order()
    {
        await AddFileAsync(Key, FileStatus.Completed, rows: Enumerable.Range(2, 120).Select(r => Processed(r, r)).ToArray());

        var second = await QueryAsync(Key, page: 2, pageSize: 50);
        var last = await QueryAsync(Key, page: 3, pageSize: 50);
        var beyond = await QueryAsync(Key, page: 4, pageSize: 50);

        Assert.Equal(Enumerable.Range(52, 50), second!.Items.Select(i => i.RowNumber));
        Assert.Equal(Enumerable.Range(102, 20), last!.Items.Select(i => i.RowNumber));
        Assert.Empty(beyond!.Items);
        Assert.Equal((120, 3), (second.TotalCount, second.PageCount));
    }

    [Fact]
    public async Task The_same_key_uploaded_again_answers_with_the_latest_file()
    {
        await AddFileAsync(Key, FileStatus.Completed, eTag: "\"v1\"", rows: [Processed(2, 20), Processed(3, 30)]);
        await AddFileAsync(Key, FileStatus.Processing, eTag: "\"v2\"", started: Started.AddHours(1), rows: [Processed(2, 99)]);

        var page = await QueryAsync(Key);

        Assert.Equal(FileProcessingStatus.Processing, page!.FileStatus);
        Assert.Equal(99, Assert.Single(page.Items).AppId);
    }

    [Fact]
    public async Task A_rejected_file_answers_failed_with_no_rows()
    {
        await AddFileAsync(Key, FileStatus.Failed);

        var page = await QueryAsync(Key);

        Assert.Equal(FileProcessingStatus.Failed, page!.FileStatus);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task An_unknown_key_answers_null()
    {
        await AddFileAsync(Key, FileStatus.Completed, rows: [Processed(2, 20)]);

        Assert.Null(await QueryAsync("exports/alice/other.xlsx"));
    }

    private async Task<FileItemsPage?> QueryAsync(string key, FileItemStatus? filter = null, int page = 1, int pageSize = 50)
    {
        await using var db = CreateDb();
        return await new FileItemsQuery(db).GetAsync(key, filter, page, pageSize, CancellationToken.None);
    }

    private static ProcessedItem Processed(int row, int appId) => new()
    {
        RowNumber = row,
        AppId = appId,
        Name = $"Game {appId}",
        Price = 9.99m,
        ReleaseDate = new DateOnly(2020, 1, 2),
        Status = ItemStatus.Processed,
        ProcessedAt = Started,
    };

    private static ProcessedItem Failed(int row, string rawPrice) => new()
    {
        RowNumber = row,
        AppId = row * 10,
        Name = $"Game {row * 10}",
        ReleaseDate = new DateOnly(2020, 1, 2),
        Status = ItemStatus.Failed,
        Error = $"Price must be a number zero or greater, found \"{rawPrice}\".",
        RawAppId = (row * 10).ToString(),
        RawName = $"Game {row * 10}",
        RawPrice = rawPrice,
        RawReleaseDate = "2020-01-02",
        ProcessedAt = Started,
    };

    private async Task AddFileAsync(
        string key,
        FileStatus status,
        string eTag = "\"etag\"",
        DateTimeOffset? started = null,
        ProcessedItem[]? rows = null)
    {
        await using var db = CreateDb();
        var file = new ProcessedFile
        {
            Bucket = "steam-items-uploads",
            Key = key,
            ETag = eTag,
            Status = status,
            StartedAt = started ?? Started,
        };
        file.Items.AddRange(rows ?? []);
        db.ProcessedFiles.Add(file);
        await db.SaveChangesAsync();
    }

    private WorkerDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<WorkerDbContext>().UseSqlite(connection).Options);

    public void Dispose() => connection.Dispose();
}
