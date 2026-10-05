using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Worker.Data;

namespace SteamItems.Worker.Status;

/// <summary>Reads the outcome of uploaded files from worker.db for the status endpoint (<see cref="FileStatusApi"/>).</summary>
public sealed class FileStatusQuery(WorkerDbContext db)
{
    /// <returns>One entry per key the Worker has a file for; unknown keys are left out.</returns>
    public async Task<IReadOnlyList<FileStatusResponse>> GetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        var files = await db.ProcessedFiles.AsNoTracking()
            .Where(f => keys.Contains(f.Key))
            .ToListAsync(cancellationToken);

        // The same key uploaded again is a new file (new ETag); the latest one is the answer.
        var latest = files
            .GroupBy(f => f.Key)
            .Select(g => g.MaxBy(f => f.StartedAt)!)
            .ToList();

        // Counts are only written when a file completes, so a file still processing reports the rows stored so far.
        var processingIds = latest.Where(f => f.Status == FileStatus.Processing).Select(f => f.Id).ToList();
        var progress = processingIds.Count == 0
            ? []
            : await db.ProcessedItems.AsNoTracking()
                .Where(i => processingIds.Contains(i.FileId))
                .GroupBy(i => new { i.FileId, i.Status })
                .Select(g => new { g.Key.FileId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken);

        return latest.Select(f =>
        {
            var (processed, failed) = f.Status == FileStatus.Processing
                ? (Count(f.Id, ItemStatus.Processed), Count(f.Id, ItemStatus.Failed))
                : (f.ProcessedCount, f.FailedCount);
            return new FileStatusResponse(f.Key, ToContract(f.Status), processed, failed, f.Error, f.StartedAt, f.CompletedAt);
        }).ToList();

        int Count(int fileId, ItemStatus status) =>
            progress.Where(p => p.FileId == fileId && p.Status == status).Sum(p => p.Count);
    }

    internal static FileProcessingStatus ToContract(FileStatus status) => status switch
    {
        FileStatus.Processing => FileProcessingStatus.Processing,
        FileStatus.Completed => FileProcessingStatus.Completed,
        FileStatus.Failed => FileProcessingStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
