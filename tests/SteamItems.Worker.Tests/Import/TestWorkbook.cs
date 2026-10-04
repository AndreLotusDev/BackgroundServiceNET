using ClosedXML.Excel;
using SteamItems.Contracts.Excel;

namespace SteamItems.Worker.Tests.Import;

/// <summary>Builds workbooks with the <see cref="ItemsWorkbook"/> layout, the way the Web writes them.</summary>
internal static class TestWorkbook
{
    public static readonly ExportInfo Info = new(
        Guid.Parse("0199b0a1-2c3d-7e4f-8a9b-0c1d2e3f4a5b"),
        "user-123",
        new DateTimeOffset(2026, 10, 4, 12, 30, 0, TimeSpan.Zero));

    public static readonly ItemRow[] Rows =
    [
        new(1245620, "Elden Ring", 59.99m, new DateOnly(2022, 2, 24)),
        new(570, "Dota 2", 0m, new DateOnly(2013, 7, 9)),
        new(413150, "Stardew Valley", 14.99m, new DateOnly(2016, 2, 26)),
    ];

    /// <param name="edit">Runs after the rows are written, to break a cell or the header.</param>
    public static byte[] Create(IEnumerable<ItemRow> rows, ExportInfo? info = null, Action<IXLWorksheet>? edit = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(ItemsWorkbook.SheetName);

        foreach (var column in ItemsWorkbook.Columns)
        {
            sheet.Cell(ItemsWorkbook.HeaderRow, column.Number).Value = column.Header;
        }

        var row = ItemsWorkbook.FirstDataRow;
        foreach (var item in rows)
        {
            sheet.Cell(row, ItemsWorkbook.AppId.Number).Value = item.AppId;
            sheet.Cell(row, ItemsWorkbook.Name.Number).Value = item.Name;
            sheet.Cell(row, ItemsWorkbook.Price.Number).Value = item.Price;
            sheet.Cell(row, ItemsWorkbook.ReleaseDate.Number).Value = item.ReleaseDate.ToDateTime(TimeOnly.MinValue);
            row++;
        }

        edit?.Invoke(sheet);

        if (info is not null)
        {
            workbook.CustomProperties.Add(ItemsWorkbook.Properties.ExportId, info.ExportId.ToString());
            workbook.CustomProperties.Add(ItemsWorkbook.Properties.UserId, info.UserId);
            workbook.CustomProperties.Add(ItemsWorkbook.Properties.CreatedAt, info.CreatedAt.ToString("O"));
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
