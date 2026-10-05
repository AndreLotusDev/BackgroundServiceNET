using SteamItems.Contracts.Excel;
using SteamItems.Worker.Import;

namespace SteamItems.Worker.Tests.Import;

public sealed class ItemsWorkbookReaderTests
{
    [Fact]
    public void Reads_typed_rows_and_the_export_properties()
    {
        var file = Read(TestWorkbook.Create(TestWorkbook.Rows, TestWorkbook.Info));

        Assert.Equal(TestWorkbook.Info.ExportId, file.ExportId);
        Assert.Equal(TestWorkbook.Info.UserId, file.UserId);
        Assert.Equal(
            TestWorkbook.Rows,
            file.Rows.Select(r => new ItemRow(r.AppId!.Value, r.Name!, r.Price!.Value, r.ReleaseDate!.Value)));
        Assert.All(file.Rows, r => Assert.True(r.IsValid));
        Assert.Equal([2, 3, 4], file.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void Missing_properties_are_null_and_the_rows_are_still_read()
    {
        var file = Read(TestWorkbook.Create(TestWorkbook.Rows));

        Assert.Null(file.ExportId);
        Assert.Null(file.UserId);
        Assert.Equal(3, file.Rows.Count);
    }

    [Fact]
    public void Blank_rows_are_skipped_and_keep_the_excel_row_numbers()
    {
        var file = Read(TestWorkbook.Create(TestWorkbook.Rows, edit: sheet => sheet.Row(3).Clear()));

        Assert.Equal([2, 4], file.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void Every_bad_cell_is_reported_on_the_row()
    {
        var file = Read(TestWorkbook.Create(TestWorkbook.Rows.Take(1), edit: sheet =>
        {
            sheet.Cell(2, ItemsWorkbook.AppId.Number).Value = 12.5;
            sheet.Cell(2, ItemsWorkbook.Name.Number).Value = " ";
            sheet.Cell(2, ItemsWorkbook.Price.Number).Value = -1;
            sheet.Cell(2, ItemsWorkbook.ReleaseDate.Number).Value = "2022-02-24";
        }));

        var row = Assert.Single(file.Rows);
        Assert.False(row.IsValid);
        // Each message names the column and the value that did not parse.
        Assert.Contains("AppId must be a positive whole number, found \"12.5\".", row.Error);
        Assert.Contains("Name must be non-empty text, found \" \".", row.Error);
        Assert.Contains("Price must be a number zero or greater, found \"-1\".", row.Error);
        Assert.Contains("ReleaseDate must be a date, found \"2022-02-24\".", row.Error);
        Assert.Equal((null, null, null, null), (row.AppId, row.Name, row.Price, row.ReleaseDate));
        Assert.Equal(new RawCells("12.5", " ", "-1", "2022-02-24"), row.Raw);
    }

    [Fact]
    public void An_empty_cell_and_a_long_value_are_described_in_the_error()
    {
        var file = Read(TestWorkbook.Create(TestWorkbook.Rows.Take(1), edit: sheet =>
        {
            sheet.Cell(2, ItemsWorkbook.Price.Number).Clear();
            sheet.Cell(2, ItemsWorkbook.ReleaseDate.Number).Value = new string('x', 100);
        }));

        var row = Assert.Single(file.Rows);
        Assert.Contains("Price must be a number zero or greater, found an empty cell.", row.Error);
        Assert.Contains($"ReleaseDate must be a date, found \"{new string('x', 40)}…\".", row.Error);
    }

    [Fact]
    public void A_workbook_without_the_items_sheet_is_rejected()
    {
        var content = TestWorkbook.Create(TestWorkbook.Rows, edit: sheet => sheet.Name = "Sheet1");

        var ex = Assert.Throws<InvalidWorkbookException>(() => Read(content));
        Assert.Contains("'Items'", ex.Message);
    }

    private static ItemsFile Read(byte[] content) => ItemsWorkbookReader.Read(new MemoryStream(content));
}
