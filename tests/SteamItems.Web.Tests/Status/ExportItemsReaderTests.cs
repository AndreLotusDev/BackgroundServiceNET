using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Web.Data;
using SteamItems.Web.Status;

namespace SteamItems.Web.Tests.Status;

public sealed class ExportItemsReaderTests : IDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeWorker worker = new();

    public ExportItemsReaderTests()
    {
        connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task A_completed_export_gets_the_requested_page_from_the_worker()
    {
        var export = await AddExportAsync("alice", ExportStatus.CompletedWithErrors);
        worker.Page = Page(export.ObjectKey, FileProcessingStatus.Completed);

        var details = await GetAsync("alice", export.Id, FileItemStatus.Failed, page: 3);

        Assert.NotNull(details);
        Assert.Equal(ExportRowsState.Available, details.State);
        Assert.Same(worker.Page, details.Rows);
        Assert.Equal((export.ObjectKey, FileItemStatus.Failed, 3, 50), Assert.Single(worker.Requests));
    }

    [Fact]
    public async Task Another_users_export_is_not_found_and_the_worker_is_not_asked()
    {
        var export = await AddExportAsync("alice", ExportStatus.Completed);

        Assert.Null(await GetAsync("bob", export.Id));
        Assert.Null(await GetAsync("alice", Guid.NewGuid()));
        Assert.Empty(worker.Requests);
    }

    [Theory]
    [InlineData(ExportStatus.Pending, ExportRowsState.Pending)]
    [InlineData(ExportStatus.Processing, ExportRowsState.Processing)]
    [InlineData(ExportStatus.Failed, ExportRowsState.Rejected)]
    public async Task An_export_that_is_not_imported_shows_a_state_without_asking_the_worker(
        ExportStatus status, ExportRowsState expected)
    {
        var export = await AddExportAsync("alice", status);

        var details = await GetAsync("alice", export.Id);

        Assert.Equal(expected, details!.State);
        Assert.Null(details.Rows);
        Assert.Empty(worker.Requests);
    }

    [Fact]
    public async Task The_worker_down_keeps_the_header_and_says_the_rows_are_unavailable()
    {
        var export = await AddExportAsync("alice", ExportStatus.Completed);
        worker.Down = true;

        var details = await GetAsync("alice", export.Id);

        Assert.Equal(ExportRowsState.WorkerUnavailable, details!.State);
        Assert.Equal(export.Id, details.Export.Id);
        Assert.Equal(3, details.Export.ProcessedCount);
    }

    [Fact]
    public async Task A_key_the_worker_does_not_know_is_missing()
    {
        var export = await AddExportAsync("alice", ExportStatus.Completed);

        var details = await GetAsync("alice", export.Id);

        Assert.Equal(ExportRowsState.Missing, details!.State);
    }

    [Fact]
    public async Task A_newer_upload_still_processing_on_the_worker_shows_processing()
    {
        var export = await AddExportAsync("alice", ExportStatus.Completed);
        worker.Page = Page(export.ObjectKey, FileProcessingStatus.Processing);

        var details = await GetAsync("alice", export.Id);

        Assert.Equal(ExportRowsState.Processing, details!.State);
        Assert.Null(details.Rows);
    }

    private async Task<ExportDetails?> GetAsync(string userId, Guid exportId, FileItemStatus? filter = null, int page = 1)
    {
        await using var db = CreateDb();
        return await new ExportItemsReader(db, worker).GetAsync(userId, exportId, filter, page, 50, CancellationToken.None);
    }

    private async Task<ExportRecord> AddExportAsync(string userId, ExportStatus status)
    {
        var id = Guid.CreateVersion7();
        var export = new ExportRecord
        {
            Id = id,
            UserId = userId,
            ObjectKey = $"exports/{userId}/{id}.xlsx",
            FileName = "steam-items.xlsx",
            ItemCount = 3,
            CreatedAt = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero),
            Status = status,
            ProcessedCount = 3,
        };
        await using var db = CreateDb();
        db.Exports.Add(export);
        await db.SaveChangesAsync();
        return export;
    }

    private static FileItemsPage Page(string key, FileProcessingStatus status) =>
        new(key, status, null, 1, 50, 1, 1, 0,
            [new FileItemResponse(2, FileItemStatus.Processed, 10, "Game", 1m, new DateOnly(2020, 1, 2), null, null)]);

    private WebDbContext CreateDb() => new(new DbContextOptionsBuilder<WebDbContext>().UseSqlite(connection).Options);

    public void Dispose() => connection.Dispose();

    private sealed class FakeWorker : IWorkerItemsClient
    {
        public FileItemsPage? Page { get; set; }

        public bool Down { get; set; }

        public List<(string Key, FileItemStatus? Filter, int Page, int PageSize)> Requests { get; } = [];

        public Task<FileItemsPage?> GetAsync(
            string key, FileItemStatus? filter, int page, int pageSize, CancellationToken cancellationToken)
        {
            Requests.Add((key, filter, page, pageSize));
            return Down
                ? throw new WorkerUnavailableException("Worker is down.")
                : Task.FromResult(Page?.Key == key ? Page : null);
        }
    }
}
