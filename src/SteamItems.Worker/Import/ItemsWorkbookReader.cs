using System.Globalization;
using ClosedXML.Excel;
using SteamItems.Contracts.Excel;

namespace SteamItems.Worker.Import;

/// <summary>What was read from an uploaded workbook.</summary>
/// <param name="ExportId">From the workbook properties; null when missing or not a GUID.</param>
public sealed record ItemsFile(Guid? ExportId, string? UserId, IReadOnlyList<ItemsFileRow> Rows);

/// <summary>
/// One data row. Valid when <see cref="Error"/> is null; then every typed value is set.
/// On an invalid row the values that did parse are kept and <see cref="Raw"/> holds every cell as text.
/// </summary>
public sealed record ItemsFileRow(
    int RowNumber,
    int? AppId,
    string? Name,
    decimal? Price,
    DateOnly? ReleaseDate,
    string? Error,
    RawCells? Raw)
{
    public bool IsValid => Error is null;
}

public sealed record RawCells(string? AppId, string? Name, string? Price, string? ReleaseDate);

/// <summary>The file is not a workbook with the <see cref="ItemsWorkbook"/> layout. Retrying will not help.</summary>
public sealed class InvalidWorkbookException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Reads files written by the Web's <c>ItemsExcelWriter</c>, using the <see cref="ItemsWorkbook"/> contract.</summary>
public static class ItemsWorkbookReader
{
    public const int MaxNameLength = 200;

    /// <exception cref="InvalidWorkbookException">Not an .xlsx, no <c>Items</c> sheet, or the header does not match.</exception>
    public static ItemsFile Read(Stream content)
    {
        using var workbook = Open(content);

        if (!workbook.TryGetWorksheet(ItemsWorkbook.SheetName, out var sheet))
        {
            throw new InvalidWorkbookException($"The workbook has no '{ItemsWorkbook.SheetName}' sheet.");
        }

        foreach (var column in ItemsWorkbook.Columns)
        {
            var header = sheet.Cell(ItemsWorkbook.HeaderRow, column.Number);
            if (!header.Value.IsText || header.GetText() != column.Header)
            {
                throw new InvalidWorkbookException(
                    $"Column {column.Number} header must be '{column.Header}', found '{Text(header)}'.");
            }
        }

        var rows = new List<ItemsFileRow>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? ItemsWorkbook.HeaderRow;
        for (var row = ItemsWorkbook.FirstDataRow; row <= lastRow; row++)
        {
            var cells = ItemsWorkbook.Columns.Select(c => sheet.Cell(row, c.Number)).ToArray();
            if (cells.All(c => c.Value.IsBlank))
            {
                continue; // A blank line in the middle is not an item.
            }

            rows.Add(ReadRow(sheet, row));
        }

        return new ItemsFile(
            Guid.TryParse(Property(workbook, ItemsWorkbook.Properties.ExportId), out var exportId) ? exportId : null,
            Property(workbook, ItemsWorkbook.Properties.UserId),
            rows);
    }

    private static XLWorkbook Open(Stream content)
    {
        try
        {
            return new XLWorkbook(content);
        }
        catch (Exception ex)
        {
            throw new InvalidWorkbookException($"The file is not a readable .xlsx workbook: {ex.Message}", ex);
        }
    }

    private static ItemsFileRow ReadRow(IXLWorksheet sheet, int row)
    {
        var errors = new List<string>();
        var appIdCell = sheet.Cell(row, ItemsWorkbook.AppId.Number);
        var nameCell = sheet.Cell(row, ItemsWorkbook.Name.Number);
        var priceCell = sheet.Cell(row, ItemsWorkbook.Price.Number);
        var releaseDateCell = sheet.Cell(row, ItemsWorkbook.ReleaseDate.Number);

        int? appId = null;
        if (appIdCell.Value.IsNumber && appIdCell.Value.GetNumber() is var number
            && number == Math.Floor(number) && number is >= 1 and <= int.MaxValue)
        {
            appId = (int)number;
        }
        else
        {
            errors.Add($"{ItemsWorkbook.AppId.Header} must be a positive whole number.");
        }

        string? name = null;
        if (nameCell.Value.IsText && !string.IsNullOrWhiteSpace(nameCell.GetText()))
        {
            name = nameCell.GetText().Trim();
            if (name.Length > MaxNameLength)
            {
                errors.Add($"{ItemsWorkbook.Name.Header} is longer than {MaxNameLength} characters.");
                name = null;
            }
        }
        else
        {
            errors.Add($"{ItemsWorkbook.Name.Header} must be non-empty text.");
        }

        decimal? price = null;
        if (priceCell.Value.IsNumber && priceCell.Value.GetNumber() >= 0)
        {
            price = (decimal)priceCell.Value.GetNumber();
        }
        else
        {
            errors.Add($"{ItemsWorkbook.Price.Header} must be a number zero or greater.");
        }

        DateOnly? releaseDate = null;
        if (releaseDateCell.Value.IsDateTime)
        {
            releaseDate = DateOnly.FromDateTime(releaseDateCell.Value.GetDateTime());
        }
        else
        {
            errors.Add($"{ItemsWorkbook.ReleaseDate.Header} must be a date.");
        }

        if (errors.Count == 0)
        {
            return new ItemsFileRow(row, appId, name, price, releaseDate, null, null);
        }

        var raw = new RawCells(Text(appIdCell), Text(nameCell), Text(priceCell), Text(releaseDateCell));
        return new ItemsFileRow(row, appId, name, price, releaseDate, string.Join(" ", errors), raw);
    }

    // The cell as the user would read it; null when empty.
    private static string? Text(IXLCell cell) =>
        cell.Value.IsBlank ? null : cell.Value.ToString(CultureInfo.InvariantCulture);

    private static string? Property(XLWorkbook workbook, string name) =>
        workbook.CustomProperties.FirstOrDefault(p => p.Name == name)?.GetValue<string>();
}
