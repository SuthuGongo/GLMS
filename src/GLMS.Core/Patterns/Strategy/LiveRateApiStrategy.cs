using System.Globalization;
using System.Net;
using System.Text.Json;
using GLMS.Core.Exceptions;

namespace GLMS.Core.Patterns.Strategy;

/// <summary>
/// CONCRETE STRATEGY #1 - fetches live rates from the free ExchangeRate-API open endpoint
/// (https://open.er-api.com/v6/latest/{BASE}) using HttpClient with async/await.
/// Any transport/HTTP/payload problem is translated into a <see cref="CurrencyConversionException"/>
/// so callers handle exactly one failure type.
///
/// Transient failures (network error, timeout, HTTP 408 or 5xx) are retried once after a short pause.
/// Permanent ones (HTTP 4xx, a bad payload) and the provider's rate limit (HTTP 429) are not retried,
/// because trying again straight away cannot succeed and would only add load.
/// </summary>
public sealed class LiveRateApiStrategy : ICurrencyConversionStrategy
{
    public const string DefaultBaseUrl = "https://open.er-api.com/v6/latest/";

    public const int DefaultMaxAttempts = 2;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(400);

    private readonly HttpClient _httpClient;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public LiveRateApiStrategy(HttpClient httpClient, int maxAttempts = DefaultMaxAttempts, TimeSpan? retryDelay = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.BaseAddress ??= new Uri(DefaultBaseUrl);
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    /// <summary>How many HTTP calls the last GetRateAsync made (1 = no retry needed).</summary>
    public int LastAttemptCount { get; private set; }

    public string Name => "ExchangeRate-API (live)";

    public async Task<ExchangeRateQuote> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        var from = CurrencyCalculator.NormaliseCurrencyCode(fromCurrency);
        var to = CurrencyCalculator.NormaliseCurrencyCode(toCurrency);

        for (var attempt = 1; ; attempt++)
        {
            LastAttemptCount = attempt;
            var isLastAttempt = attempt >= _maxAttempts;

            HttpResponseMessage response;
            try
            {
                // Relative URI -> combined with BaseAddress. ConfigureAwait(false): library code, no UI context needed.
                response = await _httpClient.GetAsync(from, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (!isLastAttempt) { await PauseAsync(ct).ConfigureAwait(false); continue; }
                throw new CurrencyConversionException("The exchange-rate service could not be reached.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (!isLastAttempt) { await PauseAsync(ct).ConfigureAwait(false); continue; }
                throw new CurrencyConversionException("The exchange-rate service timed out.", ex);
            }

            using (response)
            {
                if (IsTransient(response.StatusCode) && !isLastAttempt)
                {
                    await PauseAsync(ct).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new CurrencyConversionException("The exchange-rate service rate limit was reached. Please try again later.");

                if (!response.IsSuccessStatusCode)
                    throw new CurrencyConversionException(
                        $"The exchange-rate service returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

                await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await ParseAsync(body, from, to, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout || (int)status >= 500;

    private Task PauseAsync(CancellationToken ct) =>
        _retryDelay > TimeSpan.Zero ? Task.Delay(_retryDelay, ct) : Task.CompletedTask;

    private async Task<ExchangeRateQuote> ParseAsync(Stream body, string from, string to, CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new CurrencyConversionException("The exchange-rate service returned an unreadable response.", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("result", out var result) ||
                !string.Equals(result.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var errorType = root.TryGetProperty("error-type", out var e) ? e.GetString() : "unknown";
                throw new CurrencyConversionException($"The exchange-rate service reported an error ({errorType}).");
            }

            if (!root.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Object ||
                !rates.TryGetProperty(to, out var rateElement) || rateElement.ValueKind != JsonValueKind.Number)
            {
                throw new CurrencyConversionException($"The exchange-rate service did not return a {from}->{to} rate.");
            }

            var rate = rateElement.GetDecimal();
            if (rate <= 0)
                throw new CurrencyConversionException($"The exchange-rate service returned an invalid {from}->{to} rate ({rate.ToString(CultureInfo.InvariantCulture)}).");

            var retrievedUtc = root.TryGetProperty("time_last_update_unix", out var unix) && unix.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : DateTime.UtcNow;

            return new ExchangeRateQuote(from, to, rate, retrievedUtc, Name);
        }
    }
}
