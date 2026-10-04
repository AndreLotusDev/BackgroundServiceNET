using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Excel;
using SteamItems.Web.Data;
using SteamItems.Web.Export;

namespace SteamItems.Web.Tests.Export;

public sealed class SelectionExporterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 30, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly WebDbContext db;
    private readonly SelectionExporter exporter;

    public SelectionExporterTests()
    {
        connection.Open();
        db = new WebDbContext(new DbContextOptionsBuilder<WebDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated(); // Applies the SteamItemSeed catalog through HasData.
        exporter = new SelectionExporter(db, new ItemsExcelWriter(), new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Returns_null_when_the_user_has_no_selection()
    {
        Assert.Null(await exporter.ExportAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task Exports_only_the_users_items_sorted_by_name()
    {
        await SelectAsync("alice", 1245620, 570);
        await SelectAsync("bob", 413150);

        var export = await exporter.ExportAsync("alice", CancellationToken.None);

        Assert.NotNull(export);
        Assert.Equal("alice", export.Info.UserId);
        Assert.Equal(Now, export.Info.CreatedAt);
        Assert.Equal("steam-items-20261004-123000.xlsx", export.FileName);

        using var workbook = new XLWorkbook(new MemoryStream(export.Content));
        var sheet = workbook.Worksheet(ItemsWorkbook.SheetName);
        Assert.Equal(3, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("Dota 2", sheet.Cell(2, ItemsWorkbook.Name.Number).GetText());
        Assert.Equal("Elden Ring", sheet.Cell(3, ItemsWorkbook.Name.Number).GetText());
        Assert.Equal(export.Info.ExportId.ToString(), workbook.CustomProperty(ItemsWorkbook.Properties.ExportId).GetValue<string>());
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
