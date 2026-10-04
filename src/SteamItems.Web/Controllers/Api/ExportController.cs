using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamItems.Web.Export;
using SteamItems.Web.Storage;

namespace SteamItems.Web.Controllers.Api;

/// <summary>An export uploaded for processing.</summary>
public sealed record SubmittedExport(Guid ExportId, string ObjectKey, string FileName, int ItemCount, DateTimeOffset CreatedAt, string Status);

[ApiController]
[Route("api/items/export")]
[Authorize(Policy = AuthPolicies.ApiScope)]
public class ExportController(SelectionExporter exporter, ExportSubmitter submitter) : ControllerBase
{
    /// <summary>Downloads the caller's saved selection as an <c>.xlsx</c>; 404 when nothing is selected.</summary>
    [HttpGet]
    [Produces(ExcelExport.ContentType)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ExcelExport.ContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (userId is null)
        {
            return Forbid();
        }

        var export = await exporter.ExportAsync(userId, cancellationToken);
        return export is null
            ? NoSelection()
            : File(export.Content, ExcelExport.ContentType, export.FileName);
    }

    /// <summary>
    /// Uploads the caller's saved selection as an <c>.xlsx</c> to storage, where the Worker picks it up.
    /// 404 when nothing is selected, 503 when storage is unavailable.
    /// </summary>
    [HttpPost]
    [Produces("application/json")]
    [ProducesResponseType<SubmittedExport>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (userId is null)
        {
            return Forbid();
        }

        try
        {
            var export = await submitter.SubmitAsync(userId, cancellationToken);
            return export is null
                ? NoSelection()
                : Accepted(new SubmittedExport(
                    export.Id, export.ObjectKey, export.FileName, export.ItemCount, export.CreatedAt, export.Status.ToString()));
        }
        catch (FileStorageException)
        {
            return Problem("File storage is unavailable. Try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private ObjectResult NoSelection() =>
        Problem("No saved selection. Select items on /Items first.", statusCode: StatusCodes.Status404NotFound);
}
