using SteamItems.Contracts.Excel;
using SteamItems.Web.Data;
using SteamItems.Web.Storage;

namespace SteamItems.Web.Export;

/// <summary>Exports the user's saved selection, uploads it to storage and records it in <c>web.db</c>.</summary>
/// <remarks>The upload triggers the S3 → SQS notification that the Worker listens to.</remarks>
public sealed class ExportSubmitter(SelectionExporter exporter, IFileStorage storage, WebDbContext db)
{
    /// <summary>Object key of an export: <c>exports/{userId}/{exportId}.xlsx</c>.</summary>
    public static string ObjectKey(string userId, Guid exportId) => $"exports/{userId}/{exportId}.xlsx";

    /// <returns>The stored export, or <c>null</c> when the user has no saved selection.</returns>
    /// <exception cref="FileStorageException">The upload failed; nothing is recorded.</exception>
    public async Task<ExportRecord?> SubmitAsync(string userId, CancellationToken cancellationToken)
    {
        var export = await exporter.ExportAsync(userId, cancellationToken);
        if (export is null)
        {
            return null;
        }

        var info = export.Info;
        var key = ObjectKey(userId, info.ExportId);
        var metadata = new Dictionary<string, string>
        {
            ["export-id"] = info.ExportId.ToString(),
            ["user-id"] = info.UserId,
            ["created-at"] = info.CreatedAt.ToString("O"),
        };

        using (var content = new MemoryStream(export.Content, writable: false))
        {
            await storage.UploadAsync(key, content, ExcelExport.ContentType, metadata, cancellationToken);
        }

        // Recorded only after a successful upload, so the list never shows a file that is not in the bucket.
        var record = new ExportRecord
        {
            Id = info.ExportId,
            UserId = userId,
            ObjectKey = key,
            FileName = export.FileName,
            ItemCount = export.ItemCount,
            CreatedAt = info.CreatedAt,
            Status = ExportStatus.Pending,
        };
        db.Exports.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }
}
