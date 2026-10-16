using GLMS.Core.Exceptions;
namespace GLMS.Core.Patterns.Strategy;
public class FinancialService
{
    public const string ReportingCurrency = "ZAR";
    public const string DefaultSourceCurrency = "USD";
    private ICurrencyConversionStrategy _strategy;

    public FinancialService(ICurrencyConversionStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }
    public string CurrentStrategyName => _strategy.Name;

    public void SetStrategy(ICurrencyConversionStrategy strategy)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }
    public Task<ExchangeRateQuote> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        var from = CurrencyCalculator.NormaliseCurrencyCode(fromCurrency);
        var to = CurrencyCalculator.NormaliseCurrencyCode(toCurrency);

        if (from == to)
        {
            return Task.FromResult(new ExchangeRateQuote(from, to, 1m, DateTime.UtcNow, "Identity"));
        }

        return GetNormalisedRateAsync(from, to, ct);
    }
    private async Task<ExchangeRateQuote> GetNormalisedRateAsync(string from, string to, CancellationToken ct)
    {
        var quote = await _strategy.GetRateAsync(from, to, ct).ConfigureAwait(false);
        if (quote.Rate <= 0)
            throw new CurrencyConversionException($"Received an invalid exchange rate ({quote.Rate}).");
        return quote with { Rate = CurrencyCalculator.NormaliseRate(quote.Rate) };
    }
    public async Task<CurrencyConversion> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount cannot be negative.");

        var quote = await GetRateAsync(fromCurrency, toCurrency, ct).ConfigureAwait(false);

        if (quote.Rate <= 0)
            throw new CurrencyConversionException($"Received an invalid exchange rate ({quote.Rate}).");

        var converted = CurrencyCalculator.Convert(amount, quote.Rate);

        return new CurrencyConversion(
            quote.FromCurrency, quote.ToCurrency, amount, quote.Rate, converted,
            quote.Source, quote.RetrievedUtc, quote.IsStale);
    }
    public Task<CurrencyConversion> ConvertUsdToZarAsync(decimal amountUsd, CancellationToken ct = default) =>
        ConvertAsync(amountUsd, DefaultSourceCurrency, ReportingCurrency, ct);
}
