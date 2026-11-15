using System.Net.Http.Headers;

namespace GLMS.Web.ApiClients;

/// <summary>
/// Adds "Authorization: Bearer {jwt}" to every call the MVC app makes to the API. The token was issued by
/// POST /api/auth/login at sign-in and is kept inside the encrypted authentication cookie (never in the browser's JS).
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    public const string AccessTokenClaim = "access_token";

    private readonly IHttpContextAccessor _httpContext;

    public BearerTokenHandler(IHttpContextAccessor httpContext) => _httpContext = httpContext;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _httpContext.HttpContext?.User.FindFirst(AccessTokenClaim)?.Value;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
