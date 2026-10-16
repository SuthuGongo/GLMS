using GLMS.Core.Abstractions;
using GLMS.Core.Exceptions;

namespace GLMS.Core.Patterns.Strategy;
public sealed class FixedRateStrategy : ICurrencyConversionStrategy
{
    private readonly IReadOnlyDictionary<string, decimal> _rates;
    private readonly IClock _clock;

    /// <param name="rates">Pairs written as "USD-ZAR" mapped to the rate, e.g. { "USD-ZAR": 18.50 }.</param>
    public FixedRateStrategy(IDictionary<string, decimal> rates, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(rates);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var normalised = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pair, rate) in rates)
        {
            var parts = pair.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                throw new ArgumentException($"'{pair}' is not a currency pair. Use the form USD-ZAR.", nameof(rates));
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rates), $"The fixed rate for {pair} must be greater than zero.");

            normalised[Key(CurrencyCalculator.NormaliseCurrencyCode(parts[0]), CurrencyCalculator.NormaliseCurrencyCode(parts[1]))] = rate;
        }

        if (normalised.Count == 0)
            throw new ArgumentException("At least one fixed rate is required.", nameof(rates));

        _rates = normalised;
    }

    public string Name => "Fixed rate (appsettings.json)";

    public Task<ExchangeRateQuote> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        var from = CurrencyCalculator.NormaliseCurrencyCode(fromCurrency);
        var to = CurrencyCalculator.NormaliseCurrencyCode(toCurrency);

        if (!_rates.TryGetValue(Key(from, to), out var rate))
            throw new CurrencyConversionException($"No fixed {from}->{to} rate is configured.");

        // "Published" at the start of today, so every request costed today shares one stored rate row.
        var published = DateTime.SpecifyKind(_clock.UtcNow.Date, DateTimeKind.Utc);
        return Task.FromResult(new ExchangeRateQuote(from, to, rate, published, Name));
    }

    private static string Key(string from, string to) => $"{from}-{to}";
}
