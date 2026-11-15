using GLMS.Api.Auth;
using GLMS.Api.Mapping;
using GLMS.Api.Services;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Services;
using GLMS.Shared;
using GLMS.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Api.Controllers;

/// <summary>Service Request Processing: the Strategy (currency), Factory Method (SLA tier) and Observer rules run inside the services.</summary>
[ApiController]
[Route("api/service-requests")]
[Authorize]
public sealed class ServiceRequestsController : ControllerBase
{
    private readonly ServiceRequestService _requests;
    public ServiceRequestsController(ServiceRequestService requests) => _requests = requests;

    [HttpGet]
    [ProducesResponseType(typeof(List<ServiceRequestDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ServiceRequestDto>>> GetAll([FromQuery] ServiceRequestStatus? status, [FromQuery] int? contractId, CancellationToken ct) =>
        (await _requests.GetAllAsync(status, contractId, ct)).Select(r => r.ToDto()).ToList();

    [HttpGet("{id:int}", Name = RouteNames.GetServiceRequest)]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDto>> Get(int id, CancellationToken ct) =>
        (await _requests.GetAsync(id, ct)).ToDto();

    /// <summary>
    /// Raises a request against an Active contract. The API converts USD to ZAR with the live rate (never trusting a
    /// client-side figure). Returns 400 for Draft / On hold / Expired contracts and 503 if no exchange rate is available.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ServiceRequestDto>> Create([FromBody] CreateServiceRequestRequest request, CancellationToken ct)
    {
        var created = await _requests.CreateAsync(
            new NewServiceRequest(request.ContractId, request.Description, request.CostUsd, request.IsUrgent), ct);
        var dto = (await _requests.GetAsync(created.Id, ct)).ToDto();
        return CreatedAtRoute(RouteNames.GetServiceRequest, new { id = created.Id }, dto);
    }

    /// <summary>Moves a request on: InProgress (start work), Completed, or Cancelled (with an optional reason).</summary>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(ServiceRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestDto>> ChangeStatus(int id, [FromBody] ChangeRequestStatusRequest request, CancellationToken ct)
    {
        var updated = request.Status switch
        {
            ServiceRequestStatus.InProgress => await _requests.StartAsync(id, ct),
            ServiceRequestStatus.Completed => await _requests.CompleteAsync(id, ct),
            ServiceRequestStatus.Cancelled => await _requests.CancelAsync(id, request.Reason, ct),
            _ => throw new GLMS.Core.Exceptions.BusinessRuleException(
                "A service request can only be moved to InProgress, Completed or Cancelled. On hold is set automatically by its contract.")
        };
        return (await _requests.GetAsync(updated.Id, ct)).ToDto();
    }
}
