using GLMS.Core.Exceptions;
using GLMS.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GLMS.Api.Infrastructure;

/// <summary>
/// The ONE place where exceptions from GLMS.Core become HTTP responses (RFC 7807 problem details), so the
/// controllers contain no try/catch at all:
/// <list type="bullet">
///   <item>NotFoundException → 404 Not Found</item>
///   <item>ConcurrencyException → 409 Conflict (someone saved the contract first)</item>
///   <item>BusinessRuleException (incl. invalid status moves, invalid files) → 400 Bad Request with the messages</item>
///   <item>CurrencyConversionException → 503 Service Unavailable (no exchange rate, nothing was saved)</item>
///   <item>anything else → 500 without internal details (logged)</item>
/// </list>
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            NotFoundException ex => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = ex.Message
            },
            ConcurrencyException ex => Validation(StatusCodes.Status409Conflict, "The record was changed by someone else", ex.Errors),
            BusinessRuleException ex => Validation(StatusCodes.Status400BadRequest, "The request breaks a business rule", ex.Errors),
            CurrencyConversionException ex => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Exchange rate unavailable",
                Detail = ex.Message
            },
            BadHttpRequestException ex => new ProblemDetails
            {
                Status = ex.StatusCode,
                Title = "Bad request",
                Detail = ex.Message
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = _environment.IsDevelopment() ? exception.Message : "Something went wrong on the server. It has been logged."
            }
        };

        if (problem.Status >= 500 && exception is not CurrencyConversionException)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            _logger.LogInformation("{Method} {Path} -> {Status}: {Message}", context.Request.Method, context.Request.Path, problem.Status, exception.Message);

        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, problem.GetType(), GlmsJson.Options, "application/problem+json", ct);
        return true;
    }

    private static ValidationProblemDetails Validation(int status, string title, IReadOnlyList<string> errors) =>
        new(new Dictionary<string, string[]> { [ApiErrorKeys.General] = errors.ToArray() })
        {
            Status = status,
            Title = title,
            Detail = string.Join(" ", errors)
        };
}
