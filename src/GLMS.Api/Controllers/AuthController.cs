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

/// <summary>Sign-in. POST /api/auth/login returns a JWT to send as "Authorization: Bearer {token}".</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        if (result is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed",
                detail: "The username or password is incorrect.");
        return result;
    }

    /// <summary>Who the bearer token belongs to.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserDto> Me() => AuthService.Describe(User);
}
