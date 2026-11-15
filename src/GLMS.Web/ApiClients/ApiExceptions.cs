using System.Net;
using GLMS.Shared;

namespace GLMS.Web.ApiClients;

/// <summary>An API call did not succeed. Subclasses say why, so controllers and ApiExceptionFilter can react.</summary>
public class ApiException : Exception
{
    public ApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null) : base(message, inner) =>
        StatusCode = statusCode;

    public HttpStatusCode? StatusCode { get; }
}

/// <summary>400: the API rejected the input or a business rule (messages are safe to show).</summary>
public class ApiValidationException : ApiException
{
    public ApiValidationException(string message, IReadOnlyDictionary<string, string[]> errors, HttpStatusCode status = HttpStatusCode.BadRequest)
        : base(message, status) => Errors = errors;

    /// <summary>Field name (or "general") to messages.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public IEnumerable<string> Messages => Errors.Values.SelectMany(v => v).Distinct();

    public string AllMessages => string.Join(" ", Messages);
}

/// <summary>409: someone else saved the record first (optimistic concurrency).</summary>
public sealed class ApiConflictException : ApiValidationException
{
    public ApiConflictException(string message, IReadOnlyDictionary<string, string[]> errors)
        : base(message, errors, HttpStatusCode.Conflict) { }
}

/// <summary>404 from the API.</summary>
public sealed class ApiNotFoundException : ApiException
{
    public ApiNotFoundException(string message) : base(message, HttpStatusCode.NotFound) { }
}

/// <summary>401: the token is missing or has expired. The user has to sign in again.</summary>
public sealed class ApiUnauthorizedException : ApiException
{
    public ApiUnauthorizedException() : base("Your session has expired. Please sign in again.", HttpStatusCode.Unauthorized) { }
}

/// <summary>403: signed in, but the role does not allow this action.</summary>
public sealed class ApiForbiddenException : ApiException
{
    public ApiForbiddenException() : base("Your account is not allowed to do that.", HttpStatusCode.Forbidden) { }
}

/// <summary>The API is down, unreachable, timed out, or a dependency (e.g. the exchange-rate service) is unavailable.</summary>
public sealed class ApiUnavailableException : ApiException
{
    public ApiUnavailableException(string message, Exception? inner = null)
        : base(message, HttpStatusCode.ServiceUnavailable, inner) { }
}

internal static class ApiErrorExtensions
{
    public static bool IsGeneralKey(string key) =>
        string.IsNullOrEmpty(key) || key == ApiErrorKeys.General || key.StartsWith('$');
}
