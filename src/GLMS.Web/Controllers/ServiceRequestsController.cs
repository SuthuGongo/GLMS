using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Web.Infrastructure;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GLMS.Web.Controllers;

public class ServiceRequestsController : Controller
{
    private readonly ServiceRequestService _requests;
    private readonly ContractService _contracts;

    public ServiceRequestsController(ServiceRequestService requests, ContractService contracts)
    {
        _requests = requests;
        _contracts = contracts;
    }

    public async Task<IActionResult> Index(ServiceRequestStatus? status, CancellationToken ct) =>
        View(new ServiceRequestIndexViewModel
        {
            Status = status,
            Requests = await _requests.GetAllAsync(status, ct: ct)
        });

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        try { return View(await _requests.GetAsync(id, ct)); }
        catch (NotFoundException) { return NotFound(); }
    }

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
            // The ZAR cost is ALWAYS recalculated on the server with the current rate.
            var request = await _requests.CreateAsync(
                new NewServiceRequest(model.ContractId, model.Description, model.CostUsd!.Value, model.IsUrgent), ct);

            this.Flash($"Service request #{request.Id} raised: {DisplayFormat.Usd(request.CostUsd)} = {DisplayFormat.Zar(request.CostZar)} " +
                       $"(rate {DisplayFormat.Rate(request.ExchangeRateUsed)}). Respond by {DisplayFormat.DateTime(request.ResponseDueUtc)}.");
            return RedirectToAction(nameof(Details), new { id = request.Id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddBusinessErrors(ex);
        }
        catch (CurrencyConversionException ex)
        {
            ModelState.AddModelError(string.Empty,
                $"The request was NOT saved because the exchange rate is unavailable: {ex.Message} Please try again shortly.");
        }

        await PopulateAsync(model, ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Quote(CancellationToken ct)
    {
        try
        {
            var q = await _requests.GetUsdToZarQuoteAsync(ct);
            // rateText: the exact decimal as text, so the browser never turns it into a float.
            return Json(new
            {
                rate = q.Rate,
                rateText = q.Rate.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
                source = q.Source,
                retrievedUtc = q.RetrievedUtc,
                isStale = q.IsStale
            });
        }
        catch (CurrencyConversionException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpPost]
    public Task<IActionResult> Start(int id, CancellationToken ct) =>
        Transition(id, () => _requests.StartAsync(id, ct), "Work started.");

    [HttpPost]
    public Task<IActionResult> Complete(int id, CancellationToken ct) =>
        Transition(id, () => _requests.CompleteAsync(id, ct), "Service request completed.");

    [HttpPost]
    public Task<IActionResult> Cancel(int id, string? reason, CancellationToken ct) =>
        Transition(id, () => _requests.CancelAsync(id, reason, ct), "Service request cancelled.");

    private async Task<IActionResult> Transition(int id, Func<Task<ServiceRequest>> action, string success)
    {
        try
        {
            await action();
            this.Flash(success);
        }
        catch (NotFoundException) { return NotFound(); }
        catch (BusinessRuleException ex) { this.Flash(ex.Message, "danger"); }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateAsync(ServiceRequestCreateViewModel model, CancellationToken ct)
    {
        // Only contracts that pass the validator (Active and within term) are offered.
        var open = await _contracts.GetOpenForRequestsAsync(ct);
        model.Contracts = open.Select(c => new SelectListItem(
            $"{c.ContractNumber} - {c.Client?.Name} ({DisplayFormat.Label(c.ServiceLevel)})", c.Id.ToString()));
        model.ContractServiceLevels = open.ToDictionary(c => c.Id, c => c.ServiceLevel.ToString());

        try
        {
            model.Quote = await _requests.GetUsdToZarQuoteAsync(ct);
        }
        catch (CurrencyConversionException ex)
        {
            model.RateError = ex.Message;
        }
    }
}
