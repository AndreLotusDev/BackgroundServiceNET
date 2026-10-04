using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamItems.Web.Data;

namespace SteamItems.Web.Controllers;

[Authorize]
public class ExportsController(WebDbContext db) : Controller
{
    /// <summary>The user's uploaded exports, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");

        var exports = await db.Exports.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return View(exports);
    }
}
