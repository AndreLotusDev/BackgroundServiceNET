using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using SteamItems.Contracts.Excel;
using SteamItems.Web.Export;

namespace SteamItems.Web.Tests.Export;

public class ItemsExcelWriterTests
{
    private static readonly ExportInfo Info = new(
        Guid.Parse("0199b0a1-2c3d-7e4f-8a9b-0c1d2e3f4a5b"),
        "user-123",
        new DateTimeOffset(2026, 10, 4, 12, 30, 0, TimeSpan.Zero));

    private static readonly ItemRow[] Rows =
    [
        new(1245620, "Elden Ring", 59.99m, new DateOnly(2022, 2, 24)),
        new(570, "Dota 2", 0m, new DateOnly(2013, 7, 9)),
        new(413150, "Stardew Valley", 14.99m, new DateOnly(2016, 2, 26)),
    ];

    [Fact]
    public void Writes_one_header_row_and_one_row_per_item()
    {
        using var workbook = WriteAndOpen(Rows);

        var sheet = Assert.Single(workbook.Worksheets);
        Assert.Equal(ItemsWorkbook.SheetName, sheet.Name);
        Assert.Equal(1 + Rows.Length, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(ItemsWorkbook.Columns.Count, sheet.LastColumnUsed()!.ColumnNumber());
    }

    [Fact]
    public void Header_matches_the_contract()
    {
        using var workbook = WriteAndOpen(Rows);
        var sheet = workbook.Worksheet(ItemsWorkbook.SheetName);

        // Pinned on purpose: the Worker reads these names, so changing them is a breaking change.
        string[] expected = ["AppId", "Name", "Price", "ReleaseDate"];
        for (var column = 1; column <= expected.Length; column++)
        {
            var cell = sheet.Cell(ItemsWorkbook.HeaderRow, column);
            Assert.True(cell.Value.IsText);
            Assert.Equal(expected[column - 1], cell.GetText());
        }
    }

    [Fact]
    public void Every_data_cell_round_trips_with_its_type()
    {
        using var workbook = WriteAndOpen(Rows);
        var sheet = workbook.Worksheet(ItemsWorkbook.SheetName);

        for (var i = 0; i < Rows.Length; i++)
        {
            var expected = Rows[i];
            var row = ItemsWorkbook.FirstDataRow + i;

            var appId = sheet.Cell(row, ItemsWorkbook.AppId.Number);
            Assert.True(appId.Value.IsNumber);
            Assert.Equal(expected.AppId, appId.GetValue<int>());

            var name = sheet.Cell(row, ItemsWorkbook.Name.Number);
            Assert.True(name.Value.IsText);
            Assert.Equal(expected.Name, name.GetText());

            var price = sheet.Cell(row, ItemsWorkbook.Price.Number);
            Assert.True(price.Value.IsNumber);
            Assert.Equal(expected.Price, price.GetValue<decimal>());

            var releaseDate = sheet.Cell(row, ItemsWorkbook.ReleaseDate.Number);
            Assert.True(releaseDate.Value.IsDateTime);
            Assert.Equal(expected.ReleaseDate, DateOnly.FromDateTime(releaseDate.GetDateTime()));
        }
    }

    [Fact]
    public void Export_info_is_stored_as_document_properties()
    {
        using var workbook = WriteAndOpen(Rows);

        Assert.Equal(Info.ExportId.ToString(), workbook.CustomProperty(ItemsWorkbook.Properties.ExportId).GetValue<string>());
        Assert.Equal(Info.UserId, workbook.CustomProperty(ItemsWorkbook.Properties.UserId).GetValue<string>());
        Assert.Equal(Info.CreatedAt, DateTimeOffset.Parse(workbook.CustomProperty(ItemsWorkbook.Properties.CreatedAt).GetValue<string>()));
    }

    [Fact]
    public void No_items_writes_only_the_header()
    {
        using var workbook = WriteAndOpen([]);
        var sheet = workbook.Worksheet(ItemsWorkbook.SheetName);

        Assert.Equal(ItemsWorkbook.HeaderRow, sheet.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void File_is_a_valid_open_xml_spreadsheet()
    {
        using var stream = Write(Rows);
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);

        var errors = new OpenXmlValidator().Validate(document).Select(e => $"{e.Path?.XPath}: {e.Description}");
        Assert.Empty(errors);
    }

    private static MemoryStream Write(IEnumerable<ItemRow> rows)
    {
        var stream = new MemoryStream();
        new ItemsExcelWriter().Write(stream, rows, Info);
        stream.Position = 0;
        return stream;
    }

    private static XLWorkbook WriteAndOpen(IEnumerable<ItemRow> rows) => new(Write(rows));
}
