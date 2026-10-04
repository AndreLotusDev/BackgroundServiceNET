using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SteamItems.Contracts.Excel;
using SteamItems.Worker.Data;
using SteamItems.Worker.Import;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Tests.Import;

/// <summary>
/// Importer + real SQLite schema (the migrations, in memory) + a fake bucket.
/// Each import gets its own <see cref="WorkerDbContext"/>, like one DI scope per file in the Worker.
/// </summary>
public sealed class FileImporterTests : IDisposable
{
    private const string Bucket = "steam-items-uploads";
    private const string Key = "exports/user-123/0199b0a1-2c3d-7e4f-8a9b-0c1d2e3f4a5b.xlsx";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 13, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly FakeFileStorage storage = new();
    private readonly FileUploadedEvent upload = new(Bucket, Key);

    public FileImporterTests()
    {
        connection.Open();
        using var db = CreateDb();
        db.Database.Migrate();
    }

    [Fact]
    public async Task N_valid_rows_create_N_processed_items_and_one_completed_file()
    {
        var eTag = storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));

        await ImportAsync(upload);

        await using var db = CreateDb();
        var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
        Assert.Equal((Bucket, Key, eTag), (file.Bucket, file.Key, file.ETag));
        Assert.Equal(FileStatus.Completed, file.Status);
        Assert.Equal((3, 0), (file.ProcessedCount, file.FailedCount));
        Assert.Equal(TestWorkbook.Info.ExportId, file.ExportId);
        Assert.Equal(TestWorkbook.Info.UserId, file.UserId);
        Assert.Equal(Now, file.CompletedAt);

        var items = await db.ProcessedItems.OrderBy(i => i.RowNumber).ToListAsync();
        Assert.Equal(TestWorkbook.Rows.Length, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var expected = TestWorkbook.Rows[i];
            var item = items[i];
            Assert.Equal(file.Id, item.FileId);
            Assert.Equal(ItemsWorkbook.FirstDataRow + i, item.RowNumber);
            Assert.Equal(ItemStatus.Processed, item.Status);
            Assert.Equal(
                (expected.AppId, expected.Name, expected.Price, expected.ReleaseDate),
                (item.AppId!.Value, item.Name, item.Price!.Value, item.ReleaseDate!.Value));
            Assert.Null(item.Error);
            Assert.Null(item.RawPrice);
        }
    }

    [Fact]
    public async Task The_same_event_twice_does_not_create_duplicates()
    {
        storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));

        await ImportAsync(upload);
        await ImportAsync(upload);

        await using var db = CreateDb();
        Assert.Equal(1, await db.ProcessedFiles.CountAsync());
        Assert.Equal(TestWorkbook.Rows.Length, await db.ProcessedItems.CountAsync());
    }

    [Fact]
    public async Task A_row_with_text_in_Price_is_stored_as_failed_and_the_rest_of_the_file_continues()
    {
        // The bad row is in the middle, so the row after it shows the file kept going.
        storage.Put(Bucket, Key, TestWorkbook.Create(
            TestWorkbook.Rows,
            TestWorkbook.Info,
            sheet => sheet.Cell(3, ItemsWorkbook.Price.Number).Value = "free"));

        await ImportAsync(upload);

        await using var db = CreateDb();
        var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
        Assert.Equal(FileStatus.Completed, file.Status);
        Assert.Equal((2, 1), (file.ProcessedCount, file.FailedCount));

        var items = await db.ProcessedItems.OrderBy(i => i.RowNumber).ToListAsync();
        Assert.Equal([ItemStatus.Processed, ItemStatus.Failed, ItemStatus.Processed], items.Select(i => i.Status));

        var failed = items[1];
        Assert.Equal(3, failed.RowNumber);
        Assert.Contains("Price", failed.Error);
        Assert.Null(failed.Price);
        Assert.Equal("free", failed.RawPrice);
        // The cells that did parse are kept, typed and raw.
        Assert.Equal(570, failed.AppId);
        Assert.Equal("Dota 2", failed.Name);
        Assert.Equal("570", failed.RawAppId);
        Assert.Equal("Dota 2", failed.RawName);
        Assert.NotNull(failed.RawReleaseDate);
    }

    [Fact]
    public async Task The_same_key_uploaded_again_with_new_content_is_a_new_file()
    {
        storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));
        await ImportAsync(upload);

        storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows.Take(1), TestWorkbook.Info));
        await ImportAsync(upload);

        await using var db = CreateDb();
        var files = await db.ProcessedFiles.OrderBy(f => f.Id).ToListAsync();
        Assert.Equal(2, files.Count);
        Assert.NotEqual(files[0].ETag, files[1].ETag);
        Assert.Equal([3, 1], files.Select(f => f.ProcessedCount));
        Assert.Equal(4, await db.ProcessedItems.CountAsync());
    }

    [Fact]
    public async Task An_interrupted_file_resumes_and_keeps_the_rows_already_stored()
    {
        var content = TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info);
        var eTag = storage.Put(Bucket, Key, content);

        // What a crash after the first row leaves behind.
        int firstItemId;
        await using (var db = CreateDb())
        {
            var file = new ProcessedFile
            {
                Bucket = Bucket, Key = Key, ETag = eTag, Status = FileStatus.Processing, StartedAt = Now.AddMinutes(-5),
            };
            file.Items.Add(new ProcessedItem
            {
                RowNumber = ItemsWorkbook.FirstDataRow,
                AppId = 1245620,
                Name = "Elden Ring",
                Price = 59.99m,
                ReleaseDate = new DateOnly(2022, 2, 24),
                Status = ItemStatus.Processed,
                ProcessedAt = Now.AddMinutes(-5),
            });
            db.ProcessedFiles.Add(file);
            await db.SaveChangesAsync();
            firstItemId = file.Items[0].Id;
        }

        await ImportAsync(upload);

        await using (var db = CreateDb())
        {
            var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
            Assert.Equal(FileStatus.Completed, file.Status);
            Assert.Equal((3, 0), (file.ProcessedCount, file.FailedCount));

            var items = await db.ProcessedItems.OrderBy(i => i.RowNumber).ToListAsync();
            Assert.Equal([2, 3, 4], items.Select(i => i.RowNumber));
            Assert.Equal(firstItemId, items[0].Id);
            Assert.Equal(Now.AddMinutes(-5), items[0].ProcessedAt);
        }
    }

    [Fact]
    public async Task A_file_that_is_not_a_workbook_is_marked_failed_once_and_then_skipped()
    {
        storage.Put(Bucket, Key, "not an xlsx"u8.ToArray());

        await ImportAsync(upload);
        await ImportAsync(upload);

        await using var db = CreateDb();
        var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
        Assert.Equal(FileStatus.Failed, file.Status);
        Assert.Contains("not a readable .xlsx", file.Error);
        Assert.Empty(await db.ProcessedItems.ToListAsync());
    }

    [Fact]
    public async Task A_workbook_with_a_different_header_is_marked_failed()
    {
        storage.Put(Bucket, Key, TestWorkbook.Create(
            TestWorkbook.Rows,
            TestWorkbook.Info,
            sheet => sheet.Cell(ItemsWorkbook.HeaderRow, ItemsWorkbook.Price.Number).Value = "Cost"));

        await ImportAsync(upload);

        await using var db = CreateDb();
        var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
        Assert.Equal(FileStatus.Failed, file.Status);
        Assert.Contains("'Price'", file.Error);
        Assert.Empty(await db.ProcessedItems.ToListAsync());
    }

    [Fact]
    public async Task When_storage_is_down_the_error_propagates_and_nothing_is_stored()
    {
        storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));
        storage.IsDown = true;

        await Assert.ThrowsAsync<FileStorageException>(() => ImportAsync(upload));

        // The first try plus the retries of the storage pipeline.
        Assert.Equal(1 + TestPipelines.StorageRetries, storage.Attempts);
        await using var db = CreateDb();
        Assert.Empty(await db.ProcessedFiles.ToListAsync());
    }

    [Fact]
    public async Task A_short_storage_outage_is_retried_and_the_file_is_imported()
    {
        storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));
        storage.FailuresBeforeSuccess = 2;

        await ImportAsync(upload);

        Assert.Equal(3, storage.Attempts);
        await using var db = CreateDb();
        Assert.Equal(FileStatus.Completed, Assert.Single(await db.ProcessedFiles.ToListAsync()).Status);
    }

    [Fact]
    public async Task A_missing_object_is_not_retried()
    {
        var error = await Assert.ThrowsAsync<FileStorageException>(() => ImportAsync(upload));

        Assert.False(error.IsTransient);
        Assert.Equal(1, storage.Attempts);
    }

    [Fact]
    public async Task Cancelling_mid_file_commits_the_current_row_and_a_rerun_finishes_without_duplicates()
    {
        storage.Put(Bucket, Key, TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));
        using var shutdown = new CancellationTokenSource();

        // Ctrl+C arrives while the first row is being saved.
        var interceptor = new CancelOnFirstItemSave(shutdown);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportAsync(upload, shutdown.Token, interceptor));

        await using (var db = CreateDb())
        {
            var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
            Assert.Equal(FileStatus.Processing, file.Status);
            var item = Assert.Single(await db.ProcessedItems.ToListAsync());
            Assert.Equal(ItemsWorkbook.FirstDataRow, item.RowNumber);
        }

        await ImportAsync(upload);

        await using (var db = CreateDb())
        {
            var file = Assert.Single(await db.ProcessedFiles.ToListAsync());
            Assert.Equal(FileStatus.Completed, file.Status);
            Assert.Equal((3, 0), (file.ProcessedCount, file.FailedCount));
            Assert.Equal([2, 3, 4], (await db.ProcessedItems.OrderBy(i => i.RowNumber).ToListAsync()).Select(i => i.RowNumber));
        }
    }

    private async Task ImportAsync(
        FileUploadedEvent file, CancellationToken cancellationToken = default, IInterceptor? interceptor = null)
    {
        await using var db = CreateDb(interceptor);
        var importer = new FileImporter(
            storage, db, new FixedTimeProvider(Now), TestPipelines.Create(), NullLogger<FileImporter>.Instance);
        await importer.ImportAsync(file, cancellationToken);
    }

    private WorkerDbContext CreateDb(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<WorkerDbContext>().UseSqlite(connection);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new WorkerDbContext(options.Options);
    }

    public void Dispose() => connection.Dispose();

    /// <summary>Cancels the token right before the first item is written, as a shutdown in the middle of a row would.</summary>
    private sealed class CancelOnFirstItemSave(CancellationTokenSource source) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ProcessedItem>().Any(e => e.State == EntityState.Added))
            {
                source.Cancel();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
