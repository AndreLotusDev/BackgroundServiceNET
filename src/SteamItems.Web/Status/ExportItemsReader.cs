using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Web.Data;

namespace SteamItems.Web.Status;

/// <summary>Whether the rows of an export can be shown, and why not.</summary>
public enum ExportRowsState
{
    /// <summary><see cref="ExportDetails.Rows"/> holds the requested page.</summary>
    Available,

    /// <summary>The Worker has not picked the file up yet.</summary>
    Pending,

    /// <summary>The Worker is still storing rows; they are shown once the file is done.</summary>
    Processing,

    /// <summary>The Worker could not read the file, so it stored no rows (see <see cref="ExportRecord.Error"/>).</summary>
    Rejected,

    /// <summary>The Worker did not answer.</summary>
    WorkerUnavailable,

    /// <summary>web.db says the file was imported, but the Worker has no file for the key.</summary>
    Missing,
}

/// <param name="Rows">Only when <see cref="State"/> is <see cref="ExportRowsState.Available"/>.</param>
public sealed record ExportDetails(ExportRecord Export, ExportRowsState State, FileItemsPage? Rows);

/// <summary>
/// One export of a user with a page of its rows, read from the Worker (<see cref="IWorkerItemsClient"/>).
/// The Worker is only asked once the export is finished; the export header always comes from web.db.
/// </summary>
public sealed class ExportItemsReader(WebDbContext db, IWorkerItemsClient worker)
{
    /// <summary>The export from web.db, without asking the Worker.</summary>
    /// <returns>Null when there is no such export, or it belongs to another user.</returns>
    public Task<ExportRecord?> FindAsync(string userId, Guid exportId, CancellationToken cancellationToken) =>
        db.Exports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == exportId && e.UserId == userId, cancellationToken);

    /// <returns>Null when there is no such export, or it belongs to another user.</returns>
    public async Task<ExportDetails?> GetAsync(
        string userId,
        Guid exportId,
        FileItemStatus? filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var export = await FindAsync(userId, exportId, cancellationToken);
        if (export is null)
        {
            return null;
        }

        switch (export.Status)
        {
            case ExportStatus.Pending:
                return new ExportDetails(export, ExportRowsState.Pending, null);
            case ExportStatus.Processing:
                return new ExportDetails(export, ExportRowsState.Processing, null);
            case ExportStatus.Failed:
                return new ExportDetails(export, ExportRowsState.Rejected, null);
        }

        FileItemsPage? rows;
        try
        {
            rows = await worker.GetAsync(export.ObjectKey, filter, page, pageSize, cancellationToken);
        }
        catch (WorkerUnavailableException)
        {
            return new ExportDetails(export, ExportRowsState.WorkerUnavailable, null);
        }

        return rows?.FileStatus switch
        {
            null => new ExportDetails(export, ExportRowsState.Missing, null),
            FileProcessingStatus.Completed => new ExportDetails(export, ExportRowsState.Available, rows),
            // The key was uploaded again and the poller has not caught up yet: the Worker's latest file wins.
            FileProcessingStatus.Processing => new ExportDetails(export, ExportRowsState.Processing, null),
            FileProcessingStatus.Failed => new ExportDetails(export, ExportRowsState.Rejected, null),
            _ => throw new ArgumentOutOfRangeException(nameof(rows), rows.FileStatus, "Unknown file status."),
        };
    }
}
