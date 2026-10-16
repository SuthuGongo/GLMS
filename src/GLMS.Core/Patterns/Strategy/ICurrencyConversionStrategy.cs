namespace GLMS.Core.Patterns.Strategy;
public interface ICurrencyConversionStrategy
{
    string Name { get; }

    /// <summary>Returns how many units of <paramref name="toCurrency"/> one unit of <paramref name="fromCurrency"/> buys.</summary>
    Task<ExchangeRateQuote> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct = default);
}
public sealed record ExchangeRateQuote(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateTime RetrievedUtc,
    string Source,
    bool IsStale = false);
public sealed record CurrencyConversion(
    string FromCurrency,
    string ToCurrency,
    decimal SourceAmount,
    decimal Rate,
    decimal ConvertedAmount,
    string RateSource,
    DateTime RateRetrievedUtc,
    bool IsStale);
