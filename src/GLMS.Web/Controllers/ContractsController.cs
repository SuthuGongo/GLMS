using GLMS.Shared;
using GLMS.Web.ApiClients;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GLMS.Web.Controllers;

/// <summary>
/// Contract pages. Part 3: every read and write goes to GLMS.Api through <see cref="IContractsApi"/> (HttpClient).
/// Validation messages from the API are shown on the form; "not found", "API down" and "session expired" are
/// handled once for all controllers by ApiExceptionFilter.
/// </summary>
public class ContractsController : Controller
{
    private readonly IContractsApi _contracts;
    private readonly IClientsApi _clients;

    public ContractsController(IContractsApi contracts, IClientsApi clients)
    {
        _contracts = contracts;
        _clients = clients;
    }

    // GET /Contracts?FromDate=..&ToDate=..&Status=Active  -> GET /api/contracts?fromDate=..&toDate=..&status=Active
    public async Task<IActionResult> Index(ContractSearchViewModel search, CancellationToken ct)
    {
        search.Clients = await ClientOptionsAsync(ct);
        search.Today = DateTime.Today;

        if (ModelState.IsValid)
        {
            try
            {
                search.Results = await _contracts.SearchAsync(search.ToQuery(), ct);
            }
            catch (ApiValidationException ex) // e.g. "From" after "To"
            {
                ModelState.AddApiErrors(ex);
            }
        }

        return View(search);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct) =>
        View(new ContractDetailsViewModel { Contract = await _contracts.GetAsync(id, ct) });

    public async Task<IActionResult> Create(int? clientId, CancellationToken ct) =>
        View(new ContractFormViewModel { ClientId = clientId ?? 0, Clients = await ClientOptionsAsync(ct) });

    [HttpPost]
    [RequestSizeLimit(ValidationLimits.AgreementMaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Create(ContractFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }

        try
        {
            await using var stream = model.SignedAgreement?.OpenReadStream();
            var agreement = model.SignedAgreement is not null && stream is not null
                ? new AgreementFile(stream, model.SignedAgreement.FileName)
                : null;

            var contract = await _contracts.CreateAsync(model.ToRequest(), agreement, model.ActivateNow, ct);
            this.Flash($"Contract {contract.ContractNumber} was created as {contract.Status}.");
            return RedirectToAction(nameof(Details), new { id = contract.Id });
        }
        catch (ApiValidationException ex)
        {
            ModelState.AddApiErrors(ex, typeof(ContractFormViewModel));
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var c = await _contracts.GetAsync(id, ct);
        if (c.Status == ContractStatus.Expired)
        {
            this.Flash("Expired contracts are closed legal records and cannot be edited.", "warning");
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = ContractFormViewModel.From(c);
        model.Clients = await ClientOptionsAsync(ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ContractFormViewModel model, CancellationToken ct)
    {
        if (id != model.Id) return BadRequest();
        ModelState.Remove(nameof(model.SignedAgreement)); // the file is managed separately on Details

        if (!ModelState.IsValid)
        {
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }

        try
        {
            await _contracts.UpdateAsync(id, model.ToRequest(), ct);
            this.Flash("Contract details were updated.");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (ApiValidationException ex) // includes 409 Conflict (someone else saved first)
        {
            ModelState.AddApiErrors(ex, typeof(ContractFormViewModel));
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }
    }

    [HttpPost]
    [RequestSizeLimit(ValidationLimits.AgreementMaxBytes + 1024 * 1024)]
    public async Task<IActionResult> UploadAgreement(UploadAgreementViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid || model.SignedAgreement is null)
        {
            var message = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            this.Flash(string.IsNullOrWhiteSpace(message) ? "Choose a PDF file to upload." : message, "danger");
            return RedirectToAction(nameof(Details), new { id = model.ContractId });
        }

        try
        {
            await using var stream = model.SignedAgreement.OpenReadStream();
            await _contracts.UploadAgreementAsync(model.ContractId, new AgreementFile(stream, model.SignedAgreement.FileName), ct);
            this.Flash("Signed agreement uploaded to the file server.");
        }
        catch (ApiValidationException ex) // e.g. an .exe renamed to .pdf, rejected by the API's content check
        {
            this.Flash(ex.AllMessages, "danger");
        }

        return RedirectToAction(nameof(Details), new { id = model.ContractId });
    }

    // GET /Contracts/DownloadAgreement/5 -> GET /api/contracts/5/agreement
    public async Task<IActionResult> DownloadAgreement(int id, CancellationToken ct)
    {
        try
        {
            var file = await _contracts.DownloadAgreementAsync(id, ct);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "private, no-store";
            return File(file.Content, "application/pdf", file.FileName);
        }
        catch (ApiNotFoundException ex)
        {
            this.Flash(ex.Message, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    // POST -> PATCH /api/contracts/{id}/status (approve = Active, hold = OnHold, decline/close = Expired)
    [HttpPost]
    public async Task<IActionResult> ChangeStatus(int id, ContractStatus newStatus, byte[]? rowVersion, CancellationToken ct)
    {
        try
        {
            var contract = await _contracts.ChangeStatusAsync(id, newStatus, rowVersion, ct);
            this.Flash($"Contract {contract.ContractNumber} is now {contract.Status}.");
        }
        catch (ApiValidationException ex)
        {
            this.Flash(ex.AllMessages, "danger");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IEnumerable<SelectListItem>> ClientOptionsAsync(CancellationToken ct) =>
        (await _clients.GetAllAsync(ct)).Select(c => new SelectListItem(c.Name, c.Id.ToString()));
}
