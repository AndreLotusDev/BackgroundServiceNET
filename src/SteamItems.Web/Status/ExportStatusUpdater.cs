using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Web.Data;

namespace SteamItems.Web.Status;

/// <summary>
/// One polling round: asks the Worker about every export that is not finished, stores what changed on the
/// <see cref="ExportRecord"/> and tells the owner. Exports the Worker has not seen yet stay <see cref="ExportStatus.Pending"/>.
/// </summary>
public sealed class ExportStatusUpdater(WebDbContext db, IWorkerStatusClient worker, IExportStatusNotifier notifier)
{
    /// <returns>How many exports changed.</returns>
    /// <exception cref="WorkerUnavailableException">
    /// The Worker did not answer. Batches answered before that are already saved; the rest keep their status.
    /// </exception>
    public async Task<int> UpdateAsync(CancellationToken cancellationToken)
    {
        var open = await db.Exports
            .Where(e => e.Status == ExportStatus.Pending || e.Status == ExportStatus.Processing)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var changedCount = 0;
        foreach (var batch in open.Chunk(FileStatusApi.MaxKeys))
        {
            var statuses = await worker.GetAsync(batch.Select(e => e.ObjectKey).ToList(), cancellationToken);
            var byKey = statuses.ToDictionary(s => s.Key);

            var changed = batch
                .Where(export => byKey.TryGetValue(export.ObjectKey, out var status) && Apply(export, status))
                .ToList();
            if (changed.Count == 0)
            {
                continue;
            }

            await db.SaveChangesAsync(cancellationToken);
            changedCount += changed.Count;

            // After the save: the page renders from web.db on reload, so it never shows less than what was pushed.
            foreach (var export in changed)
            {
                await notifier.ExportChangedAsync(export, cancellationToken);
            }
        }

        return changedCount;
    }

    /// <returns>Whether anything the user sees changed.</returns>
    private static bool Apply(ExportRecord export, FileStatusResponse file)
    {
        var status = file.Status switch
        {
            FileProcessingStatus.Processing => ExportStatus.Processing,
            FileProcessingStatus.Completed when file.FailedCount > 0 => ExportStatus.CompletedWithErrors,
            FileProcessingStatus.Completed => ExportStatus.Completed,
            FileProcessingStatus.Failed => ExportStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(file), file.Status, "Unknown file status."),
        };

        if (export.Status == status
            && export.ProcessedCount == file.ProcessedCount
            && export.FailedCount == file.FailedCount
            && export.Error == file.Error
            && export.CompletedAt == file.CompletedAt)
        {
            return false;
        }

        export.Status = status;
        export.ProcessedCount = file.ProcessedCount;
        export.FailedCount = file.FailedCount;
        export.Error = file.Error;
        export.CompletedAt = file.CompletedAt;
        return true;
    }
}
