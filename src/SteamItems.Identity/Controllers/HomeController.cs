using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SteamItems.Identity.Controllers;

[AllowAnonymous]
public class HomeController(IIdentityServerInteractionService interaction) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>IdentityServer redirects here (UserInteraction.ErrorUrl) for protocol errors.</summary>
    public async Task<IActionResult> Error(string? errorId)
    {
        var message = errorId is null ? null : await interaction.GetErrorContextAsync(errorId, HttpContext.RequestAborted);
        return View(message);
    }
}
