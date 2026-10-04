using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamItems.Web.Export;

namespace SteamItems.Web.Controllers.Api;

[ApiController]
[Route("api/items/export")]
[Authorize(Policy = AuthPolicies.ApiScope)]
public class ExportController(SelectionExporter exporter) : ControllerBase
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
            ? Problem("No saved selection. Select items on /Items first.", statusCode: StatusCodes.Status404NotFound)
            : File(export.Content, ExcelExport.ContentType, export.FileName);
    }
}
