using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Files;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Services;
using GLMS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GLMS.Web.Controllers;
public class ContractsController : Controller
{
    private readonly ContractService _contracts;
    private readonly ClientService _clients;
    private readonly ServiceRequestValidator _requestValidator;
    private readonly IClock _clock;

    public ContractsController(ContractService contracts, ClientService clients, ServiceRequestValidator requestValidator, IClock clock)
    {
        _contracts = contracts;
        _clients = clients;
        _requestValidator = requestValidator;
        _clock = clock;
    }

    // GET /Contracts?FromDate=..&ToDate=..&Status=Active  - LINQ search/filter for admins
    public async Task<IActionResult> Index(ContractSearchViewModel search, CancellationToken ct)
    {
        search.Clients = await ClientOptionsAsync(ct);
        search.Today = _clock.Today;

        var criteria = search.ToCriteria();
        foreach (var error in criteria.Validate())
            ModelState.AddModelError(string.Empty, error);

        if (ModelState.IsValid)
            search.Results = await _contracts.SearchAsync(criteria, ct);

        return View(search);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        try
        {
            var contract = await _contracts.GetAsync(id, ct);
            var blockers = _requestValidator.ValidateContract(contract);
            var audit = await _contracts.GetAuditTrailAsync(id, ct);
            return View(new ContractDetailsViewModel
            {
                Contract = contract,
                AuditTrail = audit,
                NextStatuses = Contract.NextStatuses(contract.Status),
                CanRaiseRequests = blockers.Count == 0,
                RequestBlockers = blockers,
                Milestones = ContractMilestones.Build(contract, audit, _clock.UtcNow),
                Today = _clock.Today
            });
        }
        catch (NotFoundException) { return NotFound(); }
    }

    public async Task<IActionResult> Create(int? clientId, CancellationToken ct) =>
        View(new ContractFormViewModel { ClientId = clientId ?? 0, Clients = await ClientOptionsAsync(ct) });

    [HttpPost]
    [RequestSizeLimit(FileValidator.MaxFileSizeBytes + 1024 * 1024)]
    public async Task<IActionResult> Create(ContractFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }

        try
        {
            FileUpload? upload = null;
            await using var stream = model.SignedAgreement?.OpenReadStream();
            if (model.SignedAgreement is not null && stream is not null)
                upload = new FileUpload(stream, model.SignedAgreement.FileName, model.SignedAgreement.Length);

            var contract = await _contracts.CreateAsync(ToInput(model), upload, model.ActivateNow, ct);
            this.Flash($"Contract {contract.ContractNumber} was created as {contract.Status}.");
            return RedirectToAction(nameof(Details), new { id = contract.Id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddBusinessErrors(ex);
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        try
        {
            var c = await _contracts.GetAsync(id, ct);
            if (c.Status == ContractStatus.Expired)
            {
                this.Flash("Expired contracts are closed legal records and cannot be edited.", "warning");
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(new ContractFormViewModel
            {
                Id = c.Id,
                ClientId = c.ClientId,
                Title = c.Title,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                ServiceLevel = c.ServiceLevel,
                CurrentStatus = c.Status,
                RowVersion = c.RowVersion,
                Clients = await ClientOptionsAsync(ct)
            });
        }
        catch (NotFoundException) { return NotFound(); }
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
            await _contracts.UpdateAsync(id, ToInput(model), model.RowVersion, ct);
            this.Flash("Contract details were updated.");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (BusinessRuleException ex)
        {
            ModelState.AddBusinessErrors(ex);
            model.Clients = await ClientOptionsAsync(ct);
            return View(model);
        }
    }

    [HttpPost]
    [RequestSizeLimit(FileValidator.MaxFileSizeBytes + 1024 * 1024)]
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
            await _contracts.UploadSignedAgreementAsync(model.ContractId,
                new FileUpload(stream, model.SignedAgreement.FileName, model.SignedAgreement.Length), ct);
            this.Flash("Signed agreement uploaded to the file server.");
        }
        catch (NotFoundException) { return NotFound(); }
        catch (BusinessRuleException ex) // includes InvalidFileException (e.g. an .exe)
        {
            this.Flash(ex.Message, "danger");
        }

        return RedirectToAction(nameof(Details), new { id = model.ContractId });
    }

    // GET /Contracts/DownloadAgreement/5 - streams the PDF from the file server
    public async Task<IActionResult> DownloadAgreement(int id, CancellationToken ct)
    {
        try
        {
            var download = await _contracts.DownloadSignedAgreementAsync(id, ct);
            Response.Headers["X-Content-Type-Options"] = "nosniff";   // browser must treat it as a PDF, nothing else
            Response.Headers["Cache-Control"] = "private, no-store";   // signed legal documents are not cached
            return File(download.Content, "application/pdf", download.DownloadName, enableRangeProcessing: true); // MVC disposes the stream
        }
        catch (NotFoundException ex)
        {
            this.Flash(ex.Message, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ChangeStatus(int id, ContractStatus newStatus, byte[]? rowVersion, CancellationToken ct)
    {
        try
        {
            var contract = await _contracts.ChangeStatusAsync(id, newStatus, rowVersion, ct);
            this.Flash($"Contract {contract.ContractNumber} is now {contract.Status}.");
        }
        catch (NotFoundException) { return NotFound(); }
        catch (BusinessRuleException ex)
        {
            this.Flash(ex.Message, "danger");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IEnumerable<SelectListItem>> ClientOptionsAsync(CancellationToken ct) =>
        (await _clients.GetAllAsync(ct)).Select(c => new SelectListItem(c.Name, c.Id.ToString()));

    private static ContractInput ToInput(ContractFormViewModel m) =>
        new(m.ClientId, m.Title, m.StartDate, m.EndDate, m.ServiceLevel);
}
