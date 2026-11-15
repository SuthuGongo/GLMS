using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GLMS.Shared;

/// <summary>
/// Field limits used by the database columns, the server-side validators (GLMS.Core), the API request DTOs and
/// the browser checks in the MVC forms. Defined once here so every layer agrees.
/// </summary>
public static class ValidationLimits
{
    public const int ClientNameMaxLength = 150;
    public const int ClientContactMaxLength = 120;
    public const int ClientEmailMaxLength = 256;
    public const int ClientPhoneMaxLength = 30;
    public const int ClientRegionMaxLength = 80;

    /// <summary>Same shape as the CHECK constraint CK_Clients_Email_Format.</summary>
    public const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
    public const string PhonePattern = @"^\+?[0-9 ()\-]{6,30}$";

    public const int ContractTitleMaxLength = 200;
    public const int ContractMaxTermYears = 10;

    public const int RequestDescriptionMaxLength = 1000;
    public const int RequestDescriptionMinLength = 5;
    public const decimal MaxCostUsd = 10_000_000m;

    public const string AgreementExtension = ".pdf";
    public const long AgreementMaxBytes = 10 * 1024 * 1024; // 10 MB
    public const int AgreementMaxFileNameLength = 200;
}

/// <summary>
/// Pure date maths for a contract term. Used by the domain <c>Contract</c> (server) and by
/// <see cref="Dtos.ContractDto"/> (client) so the term line in the UI matches the server's rules exactly.
/// </summary>
public static class ContractTerm
{
    public static int LengthDays(DateTime start, DateTime end) => Math.Max(0, (end.Date - start.Date).Days);

    /// <summary>From 0 (not started) to 1 (ended).</summary>
    public static double Progress(DateTime start, DateTime end, DateTime today)
    {
        var total = LengthDays(start, end);
        if (total == 0) return today.Date >= end.Date ? 1 : 0;
        var elapsed = (today.Date - start.Date).TotalDays;
        return Math.Clamp(elapsed / total, 0, 1);
    }

    public static int DaysRemaining(DateTime end, DateTime today) => Math.Max(0, (end.Date - today.Date).Days);

    public static int DaysUntilStart(DateTime start, DateTime today) => Math.Max(0, (start.Date - today.Date).Days);

    public static bool IsWithinTerm(DateTime start, DateTime end, DateTime date) => date.Date >= start.Date && date.Date <= end.Date;

    /// <summary>The term has ended but the status has not caught up yet.</summary>
    public static bool IsOverdueForExpiry(ContractStatus status, DateTime end, DateTime today) =>
        (status == ContractStatus.Active || status == ContractStatus.OnHold) && end.Date < today.Date;
}

public static class RequestStatusRules
{
    public static bool IsOpen(ServiceRequestStatus status) =>
        status is ServiceRequestStatus.Pending or ServiceRequestStatus.InProgress or ServiceRequestStatus.OnHold;
}

/// <summary>
/// One JSON configuration for both services: camelCase names and enums as text. The API uses it for its
/// responses and the MVC client uses it to read them, so both always agree on the wire format.
/// </summary>
public static class GlmsJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null;   // validation error keys keep their C# property names
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping; // "isn't" instead of "isn\u0027t" (JSON API, never inlined into HTML)
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
            options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Keys used in the "errors" object of the API's ValidationProblemDetails responses.</summary>
public static class ApiErrorKeys
{
    /// <summary>Business-rule messages that are not about one field (shown in the form's summary).</summary>
    public const string General = "general";
}
