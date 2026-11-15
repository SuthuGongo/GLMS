using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GLMS.Shared;

namespace GLMS.Web.ApiClients;

/// <summary>
/// Shared plumbing for the typed HttpClients that talk to GLMS.Api. Every call goes through
/// <see cref="SendAsync"/>, which turns network failures and error status codes into the exceptions in
/// ApiExceptions.cs, so controllers deal with business outcomes, never with raw HTTP.
/// </summary>
public abstract class ApiClientBase
{
    protected ApiClientBase(HttpClient http) => Http = http;

    protected HttpClient Http { get; }

    protected Task<T> GetAsync<T>(string url, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, url), ct);

    protected Task<T> PostAsync<T>(string url, object body, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, url) { Content = Json(body) }, ct);

    protected Task<T> PutAsync<T>(string url, object body, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Put, url) { Content = Json(body) }, ct);

    protected Task<T> PatchAsync<T>(string url, object body, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Patch, url) { Content = Json(body) }, ct);

    protected async Task DeleteAsync(string url, CancellationToken ct)
    {
        using var _ = await SendRawAsync(new HttpRequestMessage(HttpMethod.Delete, url), ct);
    }

    protected async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendRawAsync(request, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(GlmsJson.Options, ct)
                   ?? throw new ApiUnavailableException("The GLMS service returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new ApiUnavailableException("The GLMS service returned a response this page could not read.", ex);
        }
    }

    /// <summary>Sends the request and returns the response only if it succeeded. The caller disposes it.</summary>
    protected async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
                response = await Http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiUnavailableException("The GLMS service can't be reached right now. Please try again in a moment.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiUnavailableException("The GLMS service took too long to answer. Please try again.", ex);
        }

        if (response.IsSuccessStatusCode) return response;

        using (response)
            throw await ToExceptionAsync(response, ct);
    }

    private static JsonContent Json(object body) => JsonContent.Create(body, body.GetType(), options: GlmsJson.Options);

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var problem = await ReadProblemAsync(response, ct);
        var message = problem?.Detail ?? problem?.Title ?? $"The GLMS service answered {(int)response.StatusCode} {response.ReasonPhrase}.";
        var errors = problem?.Errors is { Count: > 0 } e
            ? e
            : new Dictionary<string, string[]> { [ApiErrorKeys.General] = new[] { message } };

        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge
                => new ApiValidationException(message, errors),
            HttpStatusCode.Conflict => new ApiConflictException(message, errors),
            HttpStatusCode.NotFound => new ApiNotFoundException(message),
            HttpStatusCode.Unauthorized => new ApiUnauthorizedException(),
            HttpStatusCode.Forbidden => new ApiForbiddenException(),
            HttpStatusCode.ServiceUnavailable => new ApiUnavailableException(message),
            >= HttpStatusCode.InternalServerError => new ApiUnavailableException("The GLMS service hit an unexpected error. Please try again."),
            _ => new ApiException(message, response.StatusCode)
        };
    }

    private static async Task<ApiProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            if (response.Content.Headers.ContentLength == 0) return null;
            return await response.Content.ReadFromJsonAsync<ApiProblem>(GlmsJson.Options, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null; // not a problem-details body (e.g. a proxy error page)
        }
    }

    /// <summary>RFC 7807 problem details as returned by the API's GlobalExceptionHandler and [ApiController] validation.</summary>
    private sealed class ApiProblem
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public int? Status { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
