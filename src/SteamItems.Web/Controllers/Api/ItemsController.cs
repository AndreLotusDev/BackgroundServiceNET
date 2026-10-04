using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SteamItems.Web.Data;

namespace SteamItems.Web.Controllers.Api;

[ApiController]
[Route("api/items")]
[Authorize(Policy = AuthPolicies.ApiScope)]
public class ItemsController(WebDbContext db) : ControllerBase
{
    public record ItemResponse(int AppId, string Name, decimal Price, DateOnly ReleaseDate);

    // Same catalog the MVC Items page shows.
    [HttpGet]
    public async Task<IEnumerable<ItemResponse>> Get(CancellationToken cancellationToken) =>
        await db.SteamItems
            .AsNoTracking()
            .OrderBy(i => i.Name)
            .Select(i => new ItemResponse(i.AppId, i.Name, i.Price, i.ReleaseDate))
            .ToListAsync(cancellationToken);
}
