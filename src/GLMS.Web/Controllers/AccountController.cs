using System.Security.Claims;
using GLMS.Shared.Dtos;
using GLMS.Web.ApiClients;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Web.Controllers;

/// <summary>
/// Sign-in for the MVC app. The username and password are checked by the API (POST /api/auth/login), which returns a
/// JWT. The JWT is stored inside this app's encrypted, HttpOnly authentication cookie and added to every API call
/// by BearerTokenHandler, so the browser never sees the token.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    private readonly IAuthApi _auth;

    public AccountController(IAuthApi auth) => _auth = auth;

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        LoginResponse? result;
        try
        {
            result = await _auth.LoginAsync(new LoginRequest { Username = model.Username, Password = model.Password }, ct);
        }
        catch (ApiValidationException ex)
        {
            ModelState.AddApiErrors(ex, typeof(LoginViewModel));
            return View(model);
        }
        catch (ApiUnavailableException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }

        if (result is null)
        {
            ModelState.AddModelError(string.Empty, "The username or password is incorrect.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, result.Username),
            new(ClaimTypes.GivenName, result.DisplayName),
            new(ClaimTypes.Role, result.Role),
            new(BearerTokenHandler.AccessTokenClaim, result.AccessToken)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        // The cookie expires with the token, so the user is asked to sign in again instead of getting 401s.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = result.ExpiresUtc, IsPersistent = false, AllowRefresh = false });

        this.Flash($"Welcome, {result.DisplayName}.");
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        this.Flash("You have been signed out.", "info");
        return RedirectToAction(nameof(Login));
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
