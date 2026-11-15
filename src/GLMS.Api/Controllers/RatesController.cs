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

/// <summary>Exchange rates (Strategy pattern: live API with cache and stale fallback, or fixed rates).</summary>
[ApiController]
[Route("api/rates")]
[Authorize]
public sealed class RatesController : ControllerBase
{
    private readonly ServiceRequestService _requests;
    public RatesController(ServiceRequestService requests) => _requests = requests;

    [HttpGet("usd-zar")]
    [ProducesResponseType(typeof(RateQuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RateQuoteDto>> UsdToZar(CancellationToken ct) =>
        (await _requests.GetUsdToZarQuoteAsync(ct)).ToDto();
}
