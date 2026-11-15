using System.Diagnostics;
using GLMS.Web.ApiClients;
using GLMS.Web.Infrastructure;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Web.Controllers;

public class HomeController : Controller
{
    private readonly IDashboardApi _dashboard;

    public HomeController(IDashboardApi dashboard) => _dashboard = dashboard;

    /// <summary>
    /// Action-first home page. The API builds the "To do" list (WorkQueue in GLMS.Core), the contract term lines,
    /// the open-request counts and the live USD → ZAR rate in one call: GET /api/dashboard.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var d = await _dashboard.GetAsync(ct);
        return View(new DashboardViewModel
        {
            Today = d.Today,
            LocalNow = DisplayFormat.ToLocal(d.UtcNow),
            WorkItems = d.WorkItems,
            Contracts = d.Contracts,
            TotalContracts = d.TotalContracts,
            ContractsOpenForWork = d.ContractsOpenForWork,
            Requests = d.Requests,
            UsdToZar = d.UsdToZar,
            RateError = d.RateError
        });
    }

    /// <summary>
    /// Shown for 404 and other error status codes (wired up with UseStatusCodePagesWithReExecute).
    /// The original status code is kept, so browsers and search engines still see a real 404.
    /// </summary>
    [AllowAnonymous]
    [Route("/status/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusPage(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : 404;
        return code == 404 ? View("NotFound") : View("StatusError", code);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
