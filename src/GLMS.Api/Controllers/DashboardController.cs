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

/// <summary>The home page in one call: to-do list, contracts, request counts and the live rate.</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboard;
    public DashboardController(DashboardService dashboard) => _dashboard = dashboard;

    /// <summary>Everything the home page shows: the to-do list, contract term lines, open request counts and the USD to ZAR rate.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct) => await _dashboard.BuildAsync(ct);
}
