using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SteamItems.Web.Data;

namespace SteamItems.Web.Status;

/// <summary>
/// Pushes export status changes to the browser. Server to client only: the <c>/Exports</c> page listens for
/// <see cref="ExportUpdated"/>. Each user only gets their own exports (<see cref="SubjectUserIdProvider"/>).
/// </summary>
[Authorize]
public sealed class ExportsHub : Hub
{
    public const string Path = "/hubs/exports";

    /// <summary>Client method, with an <see cref="ExportStatusMessage"/>.</summary>
    public const string ExportUpdated = "exportUpdated";
}

/// <summary>What the page needs to redraw one row. Text and badge colour come from the server so the script stays small.</summary>
public sealed record ExportStatusMessage(
    Guid ExportId,
    string Status,
    string StatusText,
    string BadgeClass,
    int ProcessedCount,
    int FailedCount,
    string? Error,
    DateTimeOffset? CompletedAt)
{
    public static ExportStatusMessage From(ExportRecord export) => new(
        export.Id,
        export.Status.ToString(),
        export.Status.DisplayText(),
        export.Status.BadgeClass(),
        export.ProcessedCount,
        export.FailedCount,
        export.Error,
        export.CompletedAt);
}

/// <summary>SignalR user id = the Identity <c>sub</c> claim, the same id <see cref="ExportRecord.UserId"/> holds.</summary>
/// <remarks>The default provider reads <c>ClaimTypes.NameIdentifier</c>, which is not there because claims are not remapped.</remarks>
public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}

/// <summary>Tells the owner of an export that its status changed.</summary>
public interface IExportStatusNotifier
{
    Task ExportChangedAsync(ExportRecord export, CancellationToken cancellationToken);
}

/// <summary>Sends to every open connection of the export's owner; nothing happens if they have none.</summary>
public sealed class SignalRExportStatusNotifier(IHubContext<ExportsHub> hub, ILogger<SignalRExportStatusNotifier> logger)
    : IExportStatusNotifier
{
    public async Task ExportChangedAsync(ExportRecord export, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.User(export.UserId)
                .SendAsync(ExportsHub.ExportUpdated, ExportStatusMessage.From(export), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The status is already saved; the user sees it on the next page load.
            logger.LogWarning(ex, "Could not push the status of export {ExportId}", export.Id);
        }
    }
}
