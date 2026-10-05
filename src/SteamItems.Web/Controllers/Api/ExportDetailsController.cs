using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamItems.Contracts.Status;
using SteamItems.Web.Data;
using SteamItems.Web.Status;

namespace SteamItems.Web.Controllers.Api;

/// <summary>An export and the Worker's outcome for it.</summary>
public sealed record ExportResponse(
    Guid ExportId,
    string ObjectKey,
    string FileName,
    int ItemCount,
    DateTimeOffset CreatedAt,
    string Status,
    int ProcessedCount,
    int FailedCount,
    string? Error,
    DateTimeOffset? CompletedAt)
{
    public static ExportResponse From(ExportRecord export) => new(
        export.Id, export.ObjectKey, export.FileName, export.ItemCount, export.CreatedAt, export.Status.ToString(),
        export.ProcessedCount, export.FailedCount, export.Error, export.CompletedAt);
}

[ApiController]
[Route("api/exports")]
[Authorize(Policy = AuthPolicies.ApiScope)]
public class ExportDetailsController(ExportItemsReader itemsReader) : ControllerBase
{
    /// <summary>The caller's export with its status and counts; 404 when it is not theirs.</summary>
    [HttpGet("{id:guid}")]
    [Produces("application/json")]
    [ProducesResponseType<ExportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (userId is null)
        {
            return Forbid();
        }

        var export = await itemsReader.FindAsync(userId, id, cancellationToken);
        return export is null ? NoExport() : Ok(ExportResponse.From(export));
    }

    /// <summary>
    /// One page of the rows the Worker read from the export, in Excel row order. <c>status</c> keeps only
    /// <c>Processed</c> or <c>Failed</c> rows. 404 when the export is not the caller's, 409 while it is not imported
    /// yet or when the file was rejected, 503 when the Worker is unavailable.
    /// </summary>
    [HttpGet("{id:guid}/items")]
    [Produces("application/json")]
    [ProducesResponseType<FileItemsPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> GetItems(
        Guid id,
        FileItemStatus? status,
        [Range(1, int.MaxValue)] int page = 1,
        [Range(1, FileItemsApi.MaxPageSize)] int pageSize = FileItemsApi.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (userId is null)
        {
            return Forbid();
        }

        var details = await itemsReader.GetAsync(userId, id, status, page, pageSize, cancellationToken);
        if (details is null)
        {
            return NoExport();
        }

        return details.State switch
        {
            ExportRowsState.Available => Ok(details.Rows),
            ExportRowsState.Pending => Problem(
                "The Worker has not picked up this export yet.", statusCode: StatusCodes.Status409Conflict),
            ExportRowsState.Processing => Problem(
                "The Worker is still importing this export. Rows are available once it completes.",
                statusCode: StatusCodes.Status409Conflict),
            ExportRowsState.Rejected => Problem(
                $"The Worker could not read this export, so no rows were stored. {details.Export.Error}".TrimEnd(),
                statusCode: StatusCodes.Status409Conflict),
            ExportRowsState.Missing => Problem(
                "The Worker has no rows for this export.", statusCode: StatusCodes.Status404NotFound),
            ExportRowsState.WorkerUnavailable => Problem(
                "The rows are not available right now. Try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => throw new InvalidOperationException($"Unknown rows state {details.State}."),
        };
    }

    private ObjectResult NoExport() =>
        Problem("No such export.", statusCode: StatusCodes.Status404NotFound);
}
