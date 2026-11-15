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

/// <summary>Clients (customers). Deleting is limited to the Admin role.</summary>
[ApiController]
[Route("api/clients")]
[Authorize]
public sealed class ClientsController : ControllerBase
{
    private readonly ClientService _clients;
    public ClientsController(ClientService clients) => _clients = clients;

    [HttpGet]
    [ProducesResponseType(typeof(List<ClientDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ClientDto>>> GetAll(CancellationToken ct) =>
        (await _clients.GetAllAsync(ct)).Select(c => c.ToDto()).ToList();

    [HttpGet("{id:int}", Name = RouteNames.GetClient)]
    [ProducesResponseType(typeof(ClientDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDetailsDto>> Get(int id, CancellationToken ct) =>
        (await _clients.GetAsync(id, ct)).ToDetailsDto();

    [HttpPost]
    [ProducesResponseType(typeof(ClientDetailsDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ClientDetailsDto>> Create([FromBody] SaveClientRequest request, CancellationToken ct)
    {
        var client = await _clients.CreateAsync(request.ToInput(), ct);
        return CreatedAtRoute(RouteNames.GetClient, new { id = client.Id }, client.ToDetailsDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ClientDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDetailsDto>> Update(int id, [FromBody] SaveClientRequest request, CancellationToken ct)
    {
        await _clients.UpdateAsync(id, request.ToInput(), ct);
        return (await _clients.GetAsync(id, ct)).ToDetailsDto();
    }

    /// <summary>Deletes a client with no contracts (contracts are legal records). Admin only.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = GlmsRoles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _clients.DeleteAsync(id, ct);
        return NoContent();
    }
}
