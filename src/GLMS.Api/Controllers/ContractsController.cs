using GLMS.Api.Mapping;
using GLMS.Api.Services;
using GLMS.Core.Files;
using GLMS.Core.Services;
using GLMS.Shared;
using GLMS.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Api.Controllers;

/// <summary>
/// Contract Management Hub. Each action only translates HTTP to a GLMS.Core service call and back:
/// no SQL, no business rules. Business-rule failures are thrown by the services and turned into
/// 400 / 404 / 409 responses in one place (GlobalExceptionHandler).
/// </summary>
[ApiController]
[Route("api/contracts")]
[Authorize]
public sealed class ContractsController : ControllerBase
{
    private readonly ContractService _contracts;
    private readonly ContractReadService _reads;

    public ContractsController(ContractService contracts, ContractReadService reads)
    {
        _contracts = contracts;
        _reads = reads;
    }

    /// <summary>Lists contracts, filtered by date range, status, service level, client and keyword.</summary>
    /// <remarks>Example: GET /api/contracts?status=Active&amp;fromDate=2026-01-01&amp;toDate=2026-12-31</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(List<ContractDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<ContractDto>>> Search([FromQuery] ContractSearchQuery query, CancellationToken ct)
    {
        var results = await _contracts.SearchAsync(query.ToCriteria(), ct);
        return results.Select(c => c.ToDto()).ToList();
    }

    /// <summary>Contracts a service request can be raised against today (Active and within their term).</summary>
    [HttpGet("open-for-requests")]
    [ProducesResponseType(typeof(List<ContractDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ContractDto>>> OpenForRequests(CancellationToken ct) =>
        (await _contracts.GetOpenForRequestsAsync(ct)).Select(c => c.ToDto()).ToList();

    /// <summary>One contract with its service requests, audit trail, milestones and allowed next statuses.</summary>
    [HttpGet("{id:int}", Name = RouteNames.GetContract)]
    [ProducesResponseType(typeof(ContractDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContractDetailsDto>> Get(int id, CancellationToken ct) =>
        await _reads.GetDetailsAsync(id, ct);

    /// <summary>Creates a contract as a Draft (JSON body). Returns 201 with a Location header.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ContractDetailsDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ContractDetailsDto>> Create([FromBody] SaveContractRequest request, CancellationToken ct)
    {
        var contract = await _contracts.CreateAsync(request.ToInput(), signedAgreement: null, activate: false, ct);
        return await CreatedAsync(contract.Id, ct);
    }

    /// <summary>
    /// Creates a contract together with its signed agreement PDF (multipart/form-data) and, when
    /// ActivateNow is true, activates it in the same step. Used by the MVC "New contract" form.
    /// </summary>
    [HttpPost("with-agreement")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ValidationLimits.AgreementMaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(ContractDetailsDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ContractDetailsDto>> CreateWithAgreement([FromForm] CreateContractForm form, CancellationToken ct)
    {
        FileUpload? upload = null;
        await using var stream = form.SignedAgreement?.OpenReadStream();
        if (form.SignedAgreement is not null && stream is not null)
            upload = new FileUpload(stream, form.SignedAgreement.FileName, form.SignedAgreement.Length);

        var contract = await _contracts.CreateAsync(form.ToInput(), upload, form.ActivateNow, ct);
        return await CreatedAsync(contract.Id, ct);
    }

    /// <summary>Updates the contract's details. Send the rowVersion you read; a stale one returns 409 Conflict.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ContractDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContractDetailsDto>> Update(int id, [FromBody] SaveContractRequest request, CancellationToken ct)
    {
        await _contracts.UpdateAsync(id, request.ToInput(), request.RowVersion, ct);
        return await _reads.GetDetailsAsync(id, ct);
    }

    /// <summary>
    /// Moves the contract through the workflow (approve = Active, put on hold = OnHold, decline/close = Expired).
    /// Illegal moves (e.g. Expired to Active, or activating without the signed PDF) return 400.
    /// </summary>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(ContractDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContractDetailsDto>> ChangeStatus(int id, [FromBody] ChangeContractStatusRequest request, CancellationToken ct)
    {
        await _contracts.ChangeStatusAsync(id, request.Status!.Value, request.RowVersion, ct);
        return await _reads.GetDetailsAsync(id, ct);
    }

    /// <summary>Uploads (or replaces) the signed agreement. PDF only, 10 MB maximum; the content is checked, not just the name.</summary>
    [HttpPut("{id:int}/agreement")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ValidationLimits.AgreementMaxBytes + 1024 * 1024)]
    [ProducesResponseType(typeof(ContractDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ContractDetailsDto>> UploadAgreement(int id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        await _contracts.UploadSignedAgreementAsync(id, new FileUpload(stream, file.FileName, file.Length), ct);
        return await _reads.GetDetailsAsync(id, ct);
    }

    /// <summary>Downloads the signed agreement PDF.</summary>
    [HttpGet("{id:int}/agreement")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAgreement(int id, CancellationToken ct)
    {
        var download = await _contracts.DownloadSignedAgreementAsync(id, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return File(download.Content, "application/pdf", download.DownloadName);
    }

    /// <summary>The compliance audit trail written by the ComplianceLogger observer.</summary>
    [HttpGet("{id:int}/audit")]
    [ProducesResponseType(typeof(List<AuditEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AuditEntryDto>>> Audit(int id, CancellationToken ct)
    {
        await _contracts.GetAsync(id, ct); // 404 for an unknown contract instead of an empty list
        return (await _contracts.GetAuditTrailAsync(id, ct)).Select(a => a.ToDto()).ToList();
    }

    private async Task<ActionResult<ContractDetailsDto>> CreatedAsync(int id, CancellationToken ct)
    {
        var dto = await _reads.GetDetailsAsync(id, ct);
        return CreatedAtRoute(RouteNames.GetContract, new { id }, dto);
    }
}

/// <summary>Form fields of POST /api/contracts/with-agreement.</summary>
public sealed class CreateContractForm : SaveContractRequest
{
    /// <summary>The signed agreement (PDF). Required when ActivateNow is true.</summary>
    public IFormFile? SignedAgreement { get; set; }

    public bool ActivateNow { get; set; }
}

internal static class RouteNames
{
    public const string GetContract = "GetContract";
    public const string GetClient = "GetClient";
    public const string GetServiceRequest = "GetServiceRequest";
}
