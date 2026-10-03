using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SteamItems.Web.Controllers;

public class AccountController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        var redirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Home");
        return Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>Clears the local cookie and ends the session at IdentityServer.</summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
