using System.Diagnostics;
using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Querying;
using GLMS.Core.Services;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Web.Controllers;

public class HomeController : Controller
{
    private const int ContractsShownOnHome = 6;

    private readonly ContractService _contracts;
    private readonly ServiceRequestService _requests;
    private readonly IClock _clock;

    public HomeController(ContractService contracts, ServiceRequestService requests, IClock clock)
    {
        _contracts = contracts;
        _requests = requests;
        _clock = clock;
    }

    /// <summary>
    /// Action-first home page: a "To do" list built by WorkQueue (GLMS.Core), the contracts with their
    /// term lines, open-request counts and the live USD -> ZAR rate.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var today = _clock.Today;
        var now = _clock.UtcNow;

        var contracts = await _contracts.SearchAsync(new ContractSearchCriteria(), ct);
        var requests = await _requests.GetAllAsync(ct: ct);

        var model = new DashboardViewModel
        {
            Today = today,
            LocalNow = now.ToLocalTime(),
            WorkItems = WorkQueue.Build(contracts, requests, now, today),
            Requests = RequestSummary.Build(requests, now),
            TotalContracts = contracts.Count,
            ContractsOpenForWork = contracts.Count(c => c.Status == ContractStatus.Active && c.IsWithinTerm(today)),
            Contracts = contracts
                .Where(c => c.Status != ContractStatus.Expired || c.EndDate >= today.AddDays(-30)) // hide old closed records
                .OrderBy(c => c.Status switch
                {
                    ContractStatus.Active => 0,
                    ContractStatus.OnHold => 1,
                    ContractStatus.Draft => 2,
                    _ => 3
                })
                .ThenBy(c => c.EndDate)
                .Take(ContractsShownOnHome)
                .ToList()
        };

        try
        {
            model.UsdToZar = await _requests.GetUsdToZarQuoteAsync(ct);
        }
        catch (CurrencyConversionException ex)
        {
            model.RateError = ex.Message;
        }

        return View(model);
    }

    /// <summary>
    /// Shown for 404 and other error status codes (wired up with UseStatusCodePagesWithReExecute).
    /// The original status code is kept, so browsers and search engines still see a real 404.
    /// </summary>
    [Route("/status/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusPage(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : 404;
        return code == 404 ? View("NotFound") : View("StatusError", code);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
