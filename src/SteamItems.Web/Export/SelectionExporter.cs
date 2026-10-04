using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Excel;
using SteamItems.Web.Data;

namespace SteamItems.Web.Export;

public sealed record ExcelExport(ExportInfo Info, string FileName, int ItemCount, byte[] Content)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>Turns a user's saved selection (task 05) into an <c>.xlsx</c>.</summary>
public sealed class SelectionExporter(WebDbContext db, IItemsExcelWriter writer, TimeProvider time)
{
    /// <returns>The file, or <c>null</c> when the user has no saved selection.</returns>
    public async Task<ExcelExport?> ExportAsync(string userId, CancellationToken cancellationToken)
    {
        var rows = await db.UserSelections
            .Where(s => s.UserId == userId)
            .Select(s => s.Item)
            .OrderBy(i => i.Name)
            .Select(i => new ItemRow(i.AppId, i.Name, i.Price, i.ReleaseDate))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var info = new ExportInfo(Guid.CreateVersion7(now), userId, now);

        using var stream = new MemoryStream();
        writer.Write(stream, rows, info);
        return new ExcelExport(info, $"steam-items-{now:yyyyMMdd-HHmmss}.xlsx", rows.Count, stream.ToArray());
    }
}
