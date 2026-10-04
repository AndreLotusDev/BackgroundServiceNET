using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Excel;
using SteamItems.Web.Data;
using SteamItems.Web.Export;
using SteamItems.Web.Storage;

namespace SteamItems.Web.Tests.Export;

public sealed class ExportSubmitterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 30, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly WebDbContext db;
    private readonly FakeFileStorage storage = new();
    private readonly ExportSubmitter submitter;

    public ExportSubmitterTests()
    {
        connection.Open();
        db = new WebDbContext(new DbContextOptionsBuilder<WebDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        var exporter = new SelectionExporter(db, new ItemsExcelWriter(), new FixedTimeProvider(Now));
        submitter = new ExportSubmitter(exporter, storage, db);
    }

    [Fact]
    public async Task Uploads_the_workbook_under_the_agreed_key_and_records_it()
    {
        await SelectAsync("alice", 570, 1245620);

        var record = await submitter.SubmitAsync("alice", CancellationToken.None);

        Assert.NotNull(record);
        var upload = Assert.Single(storage.Uploads);
        Assert.Equal($"exports/alice/{record.Id}.xlsx", upload.Key);
        Assert.Equal(ExcelExport.ContentType, upload.ContentType);
        Assert.Equal(record.Id.ToString(), upload.Metadata["export-id"]);
        Assert.Equal("alice", upload.Metadata["user-id"]);
        Assert.Equal(Now.ToString("O"), upload.Metadata["created-at"]);

        // The uploaded bytes are the workbook, carrying the same export id.
        using var workbook = new XLWorkbook(new MemoryStream(upload.Content));
        Assert.Equal(3, workbook.Worksheet(ItemsWorkbook.SheetName).LastRowUsed()!.RowNumber());
        Assert.Equal(record.Id.ToString(), workbook.CustomProperty(ItemsWorkbook.Properties.ExportId).GetValue<string>());

        var saved = await db.Exports.AsNoTracking().SingleAsync();
        Assert.Equal(record.Id, saved.Id);
        Assert.Equal("alice", saved.UserId);
        Assert.Equal(upload.Key, saved.ObjectKey);
        Assert.Equal("steam-items-20261004-123000.xlsx", saved.FileName);
        Assert.Equal(2, saved.ItemCount);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(ExportStatus.Uploaded, saved.Status);
    }

    [Fact]
    public async Task Returns_null_and_uploads_nothing_without_a_selection()
    {
        Assert.Null(await submitter.SubmitAsync("nobody", CancellationToken.None));
        Assert.Empty(storage.Uploads);
        Assert.Empty(db.Exports);
    }

    [Fact]
    public async Task Records_nothing_when_the_upload_fails()
    {
        await SelectAsync("alice", 570);
        storage.FailWith = new FileStorageException("down", new IOException());

        await Assert.ThrowsAsync<FileStorageException>(() => submitter.SubmitAsync("alice", CancellationToken.None));
        Assert.Empty(db.Exports);
    }

    private async Task SelectAsync(string userId, params int[] appIds)
    {
        db.UserSelections.AddRange(appIds.Select(appId => new UserSelection { UserId = userId, AppId = appId }));
        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private sealed record Upload(string Key, string ContentType, IReadOnlyDictionary<string, string> Metadata, byte[] Content);

    private sealed class FakeFileStorage : IFileStorage
    {
        public List<Upload> Uploads { get; } = [];
        public FileStorageException? FailWith { get; set; }

        public async Task UploadAsync(string key, Stream content, string contentType,
            IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Uploads.Add(new Upload(key, contentType, metadata, copy.ToArray()));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
