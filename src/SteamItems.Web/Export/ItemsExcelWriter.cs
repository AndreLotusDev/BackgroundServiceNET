using ClosedXML.Excel;
using SteamItems.Contracts.Excel;

namespace SteamItems.Web.Export;

public interface IItemsExcelWriter
{
    /// <summary>Writes an <c>.xlsx</c> with the <see cref="ItemsWorkbook"/> layout to <paramref name="output"/>.</summary>
    void Write(Stream output, IEnumerable<ItemRow> rows, ExportInfo info);
}

public sealed class ItemsExcelWriter : IItemsExcelWriter
{
    public void Write(Stream output, IEnumerable<ItemRow> rows, ExportInfo info)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(ItemsWorkbook.SheetName);

        foreach (var column in ItemsWorkbook.Columns)
        {
            sheet.Cell(ItemsWorkbook.HeaderRow, column.Number).Value = column.Header;
        }
        sheet.Row(ItemsWorkbook.HeaderRow).Style.Font.Bold = true;

        var row = ItemsWorkbook.FirstDataRow;
        foreach (var item in rows)
        {
            sheet.Cell(row, ItemsWorkbook.AppId.Number).Value = item.AppId;
            sheet.Cell(row, ItemsWorkbook.Name.Number).Value = item.Name;
            sheet.Cell(row, ItemsWorkbook.Price.Number).Value = item.Price;
            sheet.Cell(row, ItemsWorkbook.ReleaseDate.Number).Value = item.ReleaseDate.ToDateTime(TimeOnly.MinValue);
            row++;
        }

        // Real numbers and dates (not text), so the Worker and Excel read them as typed values.
        sheet.Column(ItemsWorkbook.Price.Number).Style.NumberFormat.Format = "0.00";
        sheet.Column(ItemsWorkbook.ReleaseDate.Number).Style.NumberFormat.Format = "yyyy-mm-dd";
        sheet.SheetView.FreezeRows(ItemsWorkbook.HeaderRow);
        sheet.Columns(1, ItemsWorkbook.Columns.Count).AdjustToContents();

        // Metadata lives in the document properties so the sheet stays one header row + data rows.
        workbook.CustomProperties.Add(ItemsWorkbook.Properties.ExportId, info.ExportId.ToString());
        workbook.CustomProperties.Add(ItemsWorkbook.Properties.UserId, info.UserId);
        workbook.CustomProperties.Add(ItemsWorkbook.Properties.CreatedAt, info.CreatedAt.ToString("O"));

        workbook.SaveAs(output);
    }
}
