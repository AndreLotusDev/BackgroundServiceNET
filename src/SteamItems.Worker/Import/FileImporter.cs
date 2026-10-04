using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteamItems.Worker.Data;
using SteamItems.Worker.Messaging;
using SteamItems.Worker.Storage;

namespace SteamItems.Worker.Import;

public interface IFileImporter
{
    /// <summary>
    /// Downloads the file and stores each row in worker.db. Safe to call again for the same event:
    /// a finished file is skipped and an unfinished one resumes after its last stored row.
    /// </summary>
    /// <exception cref="FileStorageException">The file could not be downloaded; the event should be retried.</exception>
    Task ImportAsync(FileUploadedEvent upload, CancellationToken cancellationToken);
}

/// <summary>
/// Stores a file row by row. Each row is its own commit, so an interrupted file keeps the rows already stored.
/// A bad row is stored as <see cref="ItemStatus.Failed"/> and the rest of the file continues.
/// </summary>
public sealed class FileImporter(
    IFileStorage storage,
    WorkerDbContext db,
    TimeProvider timeProvider,
    ILogger<FileImporter> logger) : IFileImporter
{
    private const int SqliteConstraintError = 19;

    public async Task ImportAsync(FileUploadedEvent upload, CancellationToken cancellationToken)
    {
        var stored = await storage.DownloadAsync(upload.Bucket, upload.Key, cancellationToken);
        await using var content = stored.Content;

        var file = await db.ProcessedFiles.SingleOrDefaultAsync(
            f => f.Bucket == upload.Bucket && f.Key == upload.Key && f.ETag == stored.ETag, cancellationToken);

        if (file is { Status: not FileStatus.Processing })
        {
            logger.LogInformation(
                "Skipping {Key} (ETag {ETag}): already {Status}", upload.Key, stored.ETag, file.Status);
            return;
        }

        if (file is null)
        {
            file = await StartAsync(upload, stored.ETag, cancellationToken);
            if (file is null)
            {
                return;
            }
        }
        else
        {
            logger.LogInformation("Resuming {Key} (ETag {ETag}), file {FileId}", upload.Key, stored.ETag, file.Id);
        }

        ItemsFile workbook;
        try
        {
            workbook = ItemsWorkbookReader.Read(content);
        }
        catch (InvalidWorkbookException ex)
        {
            // Retrying would read the same bytes, so the file is closed as Failed and the message can be deleted.
            file.Status = FileStatus.Failed;
            file.Error = ex.Message;
            file.CompletedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("File {Key} rejected: {Reason}", upload.Key, ex.Message);
            return;
        }

        file.ExportId = workbook.ExportId;
        file.UserId = workbook.UserId;
        await db.SaveChangesAsync(cancellationToken);

        await StoreRowsAsync(file, workbook.Rows, cancellationToken);

        file.ProcessedCount = await CountAsync(file, ItemStatus.Processed, cancellationToken);
        file.FailedCount = await CountAsync(file, ItemStatus.Failed, cancellationToken);
        file.Status = FileStatus.Completed;
        file.CompletedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Imported {Key}: {Processed} processed, {Failed} failed (file {FileId}, export {ExportId})",
            upload.Key, file.ProcessedCount, file.FailedCount, file.Id, file.ExportId);
    }

    // Null when another processor inserted the same file first (the same event delivered twice at once).
    private async Task<ProcessedFile?> StartAsync(FileUploadedEvent upload, string eTag, CancellationToken cancellationToken)
    {
        var file = new ProcessedFile
        {
            Bucket = upload.Bucket,
            Key = upload.Key,
            ETag = eTag,
            Status = FileStatus.Processing,
            StartedAt = timeProvider.GetUtcNow(),
        };
        db.ProcessedFiles.Add(file);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsConstraintViolation(ex))
        {
            logger.LogInformation("Skipping {Key} (ETag {ETag}): another processor has it", upload.Key, eTag);
            return null;
        }

        logger.LogInformation("Importing {Key} (ETag {ETag}) as file {FileId}", upload.Key, eTag, file.Id);
        return file;
    }

    private async Task StoreRowsAsync(ProcessedFile file, IReadOnlyList<ItemsFileRow> rows, CancellationToken cancellationToken)
    {
        // Rows stored by an earlier, interrupted run of this file.
        var done = await db.ProcessedItems
            .Where(i => i.FileId == file.Id)
            .Select(i => i.RowNumber)
            .ToHashSetAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (done.Contains(row.RowNumber))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!row.IsValid)
            {
                logger.LogWarning("File {FileId} row {Row} failed: {Error}", file.Id, row.RowNumber, row.Error);
            }

            var item = ToItem(file.Id, row);
            db.ProcessedItems.Add(item);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsConstraintViolation(ex))
            {
                // A duplicate delivery of this file stored the row first; it is the same row, keep theirs.
            }
            finally
            {
                // Keeps the change tracker small on large files.
                db.Entry(item).State = EntityState.Detached;
            }
        }
    }

    private ProcessedItem ToItem(int fileId, ItemsFileRow row) => new()
    {
        FileId = fileId,
        RowNumber = row.RowNumber,
        AppId = row.AppId,
        Name = row.Name,
        Price = row.Price,
        ReleaseDate = row.ReleaseDate,
        Status = row.IsValid ? ItemStatus.Processed : ItemStatus.Failed,
        Error = row.Error,
        RawAppId = row.Raw?.AppId,
        RawName = row.Raw?.Name,
        RawPrice = row.Raw?.Price,
        RawReleaseDate = row.Raw?.ReleaseDate,
        ProcessedAt = timeProvider.GetUtcNow(),
    };

    private Task<int> CountAsync(ProcessedFile file, ItemStatus status, CancellationToken cancellationToken) =>
        db.ProcessedItems.CountAsync(i => i.FileId == file.Id && i.Status == status, cancellationToken);

    private static bool IsConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError };
}
