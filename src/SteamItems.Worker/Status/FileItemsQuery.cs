using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Worker.Data;

namespace SteamItems.Worker.Status;

/// <summary>Reads the stored rows of one uploaded file from worker.db for the rows endpoint (<see cref="FileItemsApi"/>).</summary>
public sealed class FileItemsQuery(WorkerDbContext db)
{
    /// <param name="page">1-based.</param>
    /// <returns>Null when the Worker has no file for the key.</returns>
    public async Task<FileItemsPage?> GetAsync(
        string key, FileItemStatus? filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        // The same key uploaded again is a new file (new ETag); the latest one is the answer, as in FileStatusQuery.
        var file = await db.ProcessedFiles.AsNoTracking()
            .Where(f => f.Key == key)
            .OrderByDescending(f => f.StartedAt)
            .ThenByDescending(f => f.Id)
            .Select(f => new { f.Id, f.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return null;
        }

        // Counted from the rows, not ProcessedFiles: those counts are only written when the file completes.
        var counts = await db.ProcessedItems.AsNoTracking()
            .Where(i => i.FileId == file.Id)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(c => c.Status, c => c.Count, cancellationToken);
        var processed = counts.GetValueOrDefault(ItemStatus.Processed);
        var failed = counts.GetValueOrDefault(ItemStatus.Failed);

        var rows = db.ProcessedItems.AsNoTracking().Where(i => i.FileId == file.Id);
        if (filter is { } status)
        {
            var itemStatus = ToItemStatus(status);
            rows = rows.Where(i => i.Status == itemStatus);
        }

        var items = await rows
            .OrderBy(i => i.RowNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new FileItemsPage(
            key,
            FileStatusQuery.ToContract(file.Status),
            filter,
            page,
            pageSize,
            filter switch
            {
                FileItemStatus.Processed => processed,
                FileItemStatus.Failed => failed,
                _ => processed + failed,
            },
            processed,
            failed,
            items.Select(ToContract).ToList());
    }

    private static FileItemResponse ToContract(ProcessedItem item) => new(
        item.RowNumber,
        item.Status == ItemStatus.Failed ? FileItemStatus.Failed : FileItemStatus.Processed,
        item.AppId,
        item.Name,
        item.Price,
        item.ReleaseDate,
        item.Error,
        item.Status == ItemStatus.Failed
            ? new FileItemRawCells(item.RawAppId, item.RawName, item.RawPrice, item.RawReleaseDate)
            : null);

    private static ItemStatus ToItemStatus(FileItemStatus status) => status switch
    {
        FileItemStatus.Processed => ItemStatus.Processed,
        FileItemStatus.Failed => ItemStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
