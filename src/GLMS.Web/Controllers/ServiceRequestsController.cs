using System.Globalization;
using GLMS.Shared;
using GLMS.Web.ApiClients;
using GLMS.Web.Infrastructure;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GLMS.Web.Controllers;

/// <summary>Service request pages. The USD→ZAR conversion, SLA rules and contract checks all run in the API.</summary>
public class ServiceRequestsController : Controller
{
    private readonly IServiceRequestsApi _requests;
    private readonly IContractsApi _contracts;
    private readonly IRatesApi _rates;

    public ServiceRequestsController(IServiceRequestsApi requests, IContractsApi contracts, IRatesApi rates)
    {
        _requests = requests;
        _contracts = contracts;
        _rates = rates;
    }

    public async Task<IActionResult> Index(ServiceRequestStatus? status, CancellationToken ct) =>
        View(new ServiceRequestIndexViewModel
        {
            Status = status,
            Requests = await _requests.GetAllAsync(status, ct)
        });

    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await _requests.GetAsync(id, ct));

    public async Task<IActionResult> Create(int? contractId, CancellationToken ct)
    {
        var model = new ServiceRequestCreateViewModel { ContractId = contractId ?? 0 };
        await PopulateAsync(model, ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ServiceRequestCreateViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, ct);
            return View(model);
        }

        try
        {
            // The ZAR cost is ALWAYS recalculated by the API with the current rate.
            var request = await _requests.CreateAsync(model.ToRequest(), ct);

            this.Flash($"Service request #{request.Id} raised: {DisplayFormat.Usd(request.CostUsd)} = {DisplayFormat.Zar(request.CostZar)} " +
                       $"(rate {DisplayFormat.Rate(request.ExchangeRateUsed)}). Respond by {DisplayFormat.DateTime(request.ResponseDueUtc)}.");
            return RedirectToAction(nameof(Details), new { id = request.Id });
        }
        catch (ApiValidationException ex)
        {
            ModelState.AddApiErrors(ex, typeof(ServiceRequestCreateViewModel));
        }
        catch (ApiUnavailableException ex) // 503 from the API: no exchange rate, so nothing was saved
        {
            ModelState.AddModelError(string.Empty,
                $"The request was NOT saved because the exchange rate is unavailable: {ex.Message} Please try again shortly.");
        }

        await PopulateAsync(model, ct);
        return View(model);
    }

    /// <summary>JSON for the live ZAR preview on the create page (the browser never calls the API directly).</summary>
    [HttpGet]
    public async Task<IActionResult> Quote(CancellationToken ct)
    {
        try
        {
            var q = await _rates.GetUsdToZarAsync(ct);
            // rateText: the exact decimal as text, so the browser never turns it into a float.
            return Json(new
            {
                rate = q.Rate,
                rateText = q.Rate.ToString("0.######", CultureInfo.InvariantCulture),
                source = q.Source,
                retrievedUtc = q.RetrievedUtc,
                isStale = q.IsStale
            });
        }
        catch (ApiUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost]
    public Task<IActionResult> Start(int id, CancellationToken ct) =>
        Transition(id, ServiceRequestStatus.InProgress, null, "Work started.", ct);

    [HttpPost]
    public Task<IActionResult> Complete(int id, CancellationToken ct) =>
        Transition(id, ServiceRequestStatus.Completed, null, "Service request completed.", ct);

    [HttpPost]
    public Task<IActionResult> Cancel(int id, string? reason, CancellationToken ct) =>
        Transition(id, ServiceRequestStatus.Cancelled, reason, "Service request cancelled.", ct);

    private async Task<IActionResult> Transition(int id, ServiceRequestStatus status, string? reason, string success, CancellationToken ct)
    {
        try
        {
            await _requests.ChangeStatusAsync(id, status, reason, ct);
            this.Flash(success);
        }
        catch (ApiValidationException ex) { this.Flash(ex.AllMessages, "danger"); }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateAsync(ServiceRequestCreateViewModel model, CancellationToken ct)
    {
        // Only contracts the API says are open for work (Active and within term) are offered.
        var open = await _contracts.GetOpenForRequestsAsync(ct);
        model.Contracts = open.Select(c => new SelectListItem(
            $"{c.ContractNumber} - {c.ClientName} ({DisplayFormat.Label(c.ServiceLevel)})", c.Id.ToString()));
        model.ContractServiceLevels = open.ToDictionary(c => c.Id, c => c.ServiceLevel.ToString());

        try
        {
            model.Quote = await _rates.GetUsdToZarAsync(ct);
        }
        catch (ApiUnavailableException ex)
        {
            model.RateError = ex.Message;
        }
    }
}
