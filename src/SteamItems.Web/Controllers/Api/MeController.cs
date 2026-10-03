using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SteamItems.Web.Controllers.Api;

[ApiController]
[Route("api/me")]
[Authorize(Policy = AuthPolicies.ApiScope)]
public class MeController : ControllerBase
{
    public record MeResponse(string? Subject, IEnumerable<string> Scopes);

    // Echoes the caller from the access token; a quick way to check the Swagger login.
    [HttpGet]
    public ActionResult<MeResponse> Get() => new MeResponse(
        User.FindFirst("sub")?.Value,
        User.FindAll("scope").Select(c => c.Value));
}
