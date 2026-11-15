using GLMS.Web.ApiClients;
using GLMS.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GLMS.Web.Filters;

/// <summary>
/// Handles failed API calls the same way on every page, so controllers only catch the outcomes they can act on
/// (validation messages):
/// <list type="bullet">
///   <item>API unreachable / timed out / 5xx → friendly "service unavailable" page (503)</item>
///   <item>401 (token expired) → sign out and back to the login page</item>
///   <item>403 → message on the previous page</item>
///   <item>404 → the normal "not found" page</item>
/// </list>
/// </summary>
public sealed class ApiExceptionFilter : IAsyncExceptionFilter
{
    private readonly ITempDataDictionaryFactory _tempData;
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ITempDataDictionaryFactory tempData, ILogger<ApiExceptionFilter> logger)
    {
        _tempData = tempData;
        _logger = logger;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not ApiException ex) return;

        var http = context.HttpContext;
        var tempData = _tempData.GetTempData(http);

        switch (ex)
        {
            case ApiUnauthorizedException:
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                tempData[ControllerExtensions.FlashMessageKey] = ex.Message;
                tempData[ControllerExtensions.FlashTypeKey] = "warning";
                context.Result = new RedirectToActionResult("Login", "Account", new { returnUrl = http.Request.Path + http.Request.QueryString });
                break;

            case ApiForbiddenException:
                tempData[ControllerExtensions.FlashMessageKey] = ex.Message;
                tempData[ControllerExtensions.FlashTypeKey] = "danger";
                var referer = http.Request.Headers.Referer.ToString();
                context.Result = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == http.Request.Host.Host
                    ? new LocalRedirectResult(uri.PathAndQuery)
                    : new RedirectToActionResult("Index", "Home", null);
                break;

            case ApiNotFoundException:
                context.Result = new NotFoundResult();   // re-executed as the friendly NotFound page
                break;

            case ApiUnavailableException:
                _logger.LogWarning(ex, "GLMS API unavailable for {Path}", http.Request.Path);
                context.Result = new ViewResult
                {
                    ViewName = "ServiceUnavailable",
                    StatusCode = StatusCodes.Status503ServiceUnavailable,
                    ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState)
                    {
                        Model = ex.Message
                    }
                };
                break;

            default:
                return; // anything else is a genuine bug: let the normal error page handle it
        }

        context.ExceptionHandled = true;
    }
}
