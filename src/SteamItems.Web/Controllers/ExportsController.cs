using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamItems.Contracts.Status;
using SteamItems.Web.Data;
using SteamItems.Web.Status;

namespace SteamItems.Web.Controllers;

[Authorize]
public class ExportsController(WebDbContext db, ExportItemsReader itemsReader) : Controller
{
    /// <summary>The user's uploaded exports, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var exports = await db.Exports.AsNoTracking()
            .Where(e => e.UserId == UserId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return View(exports);
    }

    /// <summary>One export with a page of its rows. 404 when it is not the user's.</summary>
    [HttpGet("Exports/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, FileItemStatus? status, int page = 1, CancellationToken cancellationToken = default)
    {
        var details = await itemsReader.GetAsync(
            UserId, id, status, Math.Max(page, 1), FileItemsApi.DefaultPageSize, cancellationToken);

        return details is null ? NotFound() : View(details);
    }

    private string UserId => User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");
}
