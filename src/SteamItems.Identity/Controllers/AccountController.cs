using Duende.IdentityServer.Events;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SteamItems.Identity.Models;

namespace SteamItems.Identity.Controllers;

[AllowAnonymous]
public class AccountController(
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    IIdentityServerInteractionService interaction,
    IEventService events) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(model.UserName, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            await events.RaiseAsync(new UserLoginFailureEvent(model.UserName, "invalid credentials"), HttpContext.RequestAborted);
            ModelState.AddModelError(string.Empty, result.IsLockedOut ? "Account locked. Try again later." : "Invalid username or password.");
            return View(model);
        }

        var user = await userManager.FindByNameAsync(model.UserName);
        await events.RaiseAsync(new UserLoginSuccessEvent(user!.UserName, user.Id, user.UserName), HttpContext.RequestAborted);

        return RedirectToReturnUrl(model.ReturnUrl);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl)
    {
        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new IdentityUser { UserName = model.UserName, Email = model.Email };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToReturnUrl(model.ReturnUrl);
    }

    /// <summary>
    /// IdentityServer's end_session endpoint redirects here with a logoutId.
    /// No confirmation prompt: sign out and go straight back to the client.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Logout(string? logoutId)
    {
        var context = await interaction.GetLogoutContextAsync(logoutId, HttpContext.RequestAborted);

        if (User.Identity?.IsAuthenticated == true)
        {
            await signInManager.SignOutAsync();
            await events.RaiseAsync(new UserLogoutSuccessEvent(User.FindFirst("sub")?.Value, User.Identity.Name), HttpContext.RequestAborted);
        }

        return View("LoggedOut", new LoggedOutViewModel
        {
            PostLogoutRedirectUri = context?.PostLogoutRedirectUri,
            ClientName = context?.ClientName ?? context?.ClientId,
            SignOutIframeUrl = context?.SignOutIFrameUrl,
        });
    }

    private IActionResult RedirectToReturnUrl(string? returnUrl)
    {
        // Only follow URLs that belong to an authorize request or to this site.
        if (interaction.IsValidReturnUrl(returnUrl) || Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl!);
        }
        return Redirect("~/");
    }
}
