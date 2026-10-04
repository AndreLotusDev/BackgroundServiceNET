using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Web.Data;
using SteamItems.Web.Status;

namespace SteamItems.Web.Tests.Status;

public sealed class ExportStatusUpdaterTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Done = Created.AddMinutes(2);

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeWorker worker = new();
    private readonly FakeNotifier notifier = new();

    public ExportStatusUpdaterTests()
    {
        connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task A_finished_file_marks_the_export_completed_with_counts_and_tells_the_owner()
    {
        var export = await AddExportAsync("alice");
        worker.Set(Completed(export.ObjectKey, processed: 3, failed: 0));

        Assert.Equal(1, await UpdateAsync());

        var saved = await LoadAsync(export.Id);
        Assert.Equal(ExportStatus.Completed, saved.Status);
        Assert.Equal((3, 0), (saved.ProcessedCount, saved.FailedCount));
        Assert.Equal(Done, saved.CompletedAt);
        Assert.Null(saved.Error);

        var pushed = Assert.Single(notifier.Changed);
        Assert.Equal((export.Id, "alice", ExportStatus.Completed), (pushed.Id, pushed.UserId, pushed.Status));
    }

    [Fact]
    public async Task Failed_rows_mark_the_export_completed_with_errors()
    {
        var export = await AddExportAsync("alice");
        worker.Set(Completed(export.ObjectKey, processed: 2, failed: 1));

        await UpdateAsync();

        var saved = await LoadAsync(export.Id);
        Assert.Equal(ExportStatus.CompletedWithErrors, saved.Status);
        Assert.Equal("Completed with errors", saved.Status.DisplayText());
        Assert.Equal((2, 1), (saved.ProcessedCount, saved.FailedCount));
    }

    [Fact]
    public async Task A_rejected_file_marks_the_export_failed_with_the_reason()
    {
        var export = await AddExportAsync("alice");
        worker.Set(new FileStatusResponse(
            export.ObjectKey, FileProcessingStatus.Failed, 0, 0, "Sheet 'Items' not found.", Created, Done));

        await UpdateAsync();

        var saved = await LoadAsync(export.Id);
        Assert.Equal(ExportStatus.Failed, saved.Status);
        Assert.Equal("Sheet 'Items' not found.", saved.Error);
    }

    [Fact]
    public async Task Progress_is_pushed_while_processing_and_unchanged_progress_is_not_pushed_again()
    {
        var export = await AddExportAsync("alice");

        worker.Set(Processing(export.ObjectKey, processed: 100));
        await UpdateAsync();
        await UpdateAsync();
        worker.Set(Processing(export.ObjectKey, processed: 250));
        await UpdateAsync();

        Assert.Equal([100, 250], notifier.Changed.Select(e => e.ProcessedCount));
        Assert.Equal(ExportStatus.Processing, (await LoadAsync(export.Id)).Status);
    }

    [Fact]
    public async Task An_export_the_worker_has_not_seen_stays_pending()
    {
        var export = await AddExportAsync("alice");

        Assert.Equal(0, await UpdateAsync());

        Assert.Equal(ExportStatus.Pending, (await LoadAsync(export.Id)).Status);
        Assert.Equal([export.ObjectKey], Assert.Single(worker.Requests));
        Assert.Empty(notifier.Changed);
    }

    [Fact]
    public async Task While_the_worker_is_down_the_export_stays_pending_and_updates_once_it_is_back()
    {
        var export = await AddExportAsync("alice");
        worker.Set(Completed(export.ObjectKey, processed: 3, failed: 0));
        worker.Down = true;

        await Assert.ThrowsAsync<WorkerUnavailableException>(UpdateAsync);
        Assert.Equal(ExportStatus.Pending, (await LoadAsync(export.Id)).Status);
        Assert.Empty(notifier.Changed);

        worker.Down = false;
        await UpdateAsync();

        Assert.Equal(ExportStatus.Completed, (await LoadAsync(export.Id)).Status);
        Assert.Single(notifier.Changed);
    }

    [Fact]
    public async Task Finished_exports_are_not_asked_about_again()
    {
        var export = await AddExportAsync("alice");
        worker.Set(Completed(export.ObjectKey, processed: 3, failed: 0));
        await UpdateAsync();
        worker.Requests.Clear();

        Assert.Equal(0, await UpdateAsync());

        Assert.Empty(worker.Requests);
    }

    [Fact]
    public async Task Many_open_exports_are_asked_about_in_batches()
    {
        var count = FileStatusApi.MaxKeys + 1;
        for (var i = 0; i < count; i++)
        {
            await AddExportAsync("alice", Created.AddSeconds(i));
        }

        await UpdateAsync();

        Assert.Equal([FileStatusApi.MaxKeys, 1], worker.Requests.Select(r => r.Count));
    }

    private async Task<int> UpdateAsync()
    {
        await using var db = CreateDb();
        return await new ExportStatusUpdater(db, worker, notifier).UpdateAsync(CancellationToken.None);
    }

    private async Task<ExportRecord> AddExportAsync(string userId, DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        var export = new ExportRecord
        {
            Id = id,
            UserId = userId,
            ObjectKey = $"exports/{userId}/{id}.xlsx",
            FileName = "steam-items.xlsx",
            ItemCount = 3,
            CreatedAt = createdAt ?? Created,
            Status = ExportStatus.Pending,
        };
        await using var db = CreateDb();
        db.Exports.Add(export);
        await db.SaveChangesAsync();
        return export;
    }

    private async Task<ExportRecord> LoadAsync(Guid id)
    {
        await using var db = CreateDb();
        return await db.Exports.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private static FileStatusResponse Completed(string key, int processed, int failed) =>
        new(key, FileProcessingStatus.Completed, processed, failed, null, Created, Done);

    private static FileStatusResponse Processing(string key, int processed) =>
        new(key, FileProcessingStatus.Processing, processed, 0, null, Created, null);

    private WebDbContext CreateDb() => new(new DbContextOptionsBuilder<WebDbContext>().UseSqlite(connection).Options);

    public void Dispose() => connection.Dispose();

    private sealed class FakeWorker : IWorkerStatusClient
    {
        private readonly Dictionary<string, FileStatusResponse> files = [];

        public bool Down { get; set; }
        public List<IReadOnlyCollection<string>> Requests { get; } = [];

        public void Set(FileStatusResponse file) => files[file.Key] = file;

        public Task<IReadOnlyList<FileStatusResponse>> GetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new WorkerUnavailableException("down");
            }

            Requests.Add(keys);
            IReadOnlyList<FileStatusResponse> known = keys.Where(files.ContainsKey).Select(k => files[k]).ToList();
            return Task.FromResult(known);
        }
    }

    private sealed class FakeNotifier : IExportStatusNotifier
    {
        // Copies: the updater keeps changing the tracked entity on later rounds.
        public List<ExportRecord> Changed { get; } = [];

        public Task ExportChangedAsync(ExportRecord export, CancellationToken cancellationToken)
        {
            Changed.Add(new ExportRecord
            {
                Id = export.Id,
                UserId = export.UserId,
                ObjectKey = export.ObjectKey,
                FileName = export.FileName,
                Status = export.Status,
                ProcessedCount = export.ProcessedCount,
                FailedCount = export.FailedCount,
                Error = export.Error,
                CompletedAt = export.CompletedAt,
            });
            return Task.CompletedTask;
        }
    }
}
