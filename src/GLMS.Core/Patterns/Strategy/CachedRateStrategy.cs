using System.Collections.Concurrent;
using GLMS.Core.Abstractions;
using GLMS.Core.Exceptions;

namespace GLMS.Core.Patterns.Strategy;
public sealed class ExchangeRateCache
{
    private readonly ConcurrentDictionary<string, ExchangeRateQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _cachedAtUtc = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string from, string to, out ExchangeRateQuote quote, out DateTime cachedAtUtc)
    {
        var key = Key(from, to);
        cachedAtUtc = default;
        return _quotes.TryGetValue(key, out quote!) && _cachedAtUtc.TryGetValue(key, out cachedAtUtc);
    }
    public void Set(ExchangeRateQuote quote, DateTime cachedAtUtc)
    {
        var key = Key(quote.FromCurrency, quote.ToCurrency);
        _quotes[key] = quote;
        _cachedAtUtc[key] = cachedAtUtc;
    }
    public void Clear()
    {
        _quotes.Clear();
        _cachedAtUtc.Clear();
    }

    private static string Key(string from, string to) => $"{from}->{to}";
}

/// <summary>
/// CONCRETE STRATEGY #2 - serves rates from cache and only calls the wrapped strategy when the
/// cached quote is older than the time-to-live. If the live source is DOWN it falls back to the
/// last known rate (flagged as stale) instead of failing the whole request - the availability
/// fallback described in Part 1 section 2.1 / 3.1.
///
/// It is itself an <see cref="ICurrencyConversionStrategy"/>, so FinancialService cannot tell the
/// difference - it simply works with "a strategy".
/// </summary>
public sealed class CachedRateStrategy : ICurrencyConversionStrategy
{
    private readonly ICurrencyConversionStrategy _inner;
    private readonly ExchangeRateCache _cache;
    private readonly IClock _clock;
    private readonly TimeSpan _timeToLive;
    private readonly TimeSpan _maxStaleAge;

    /// <summary>Oldest a "last known rate" may be when the live service is down. Older than this, costing stops.</summary>
    public static readonly TimeSpan DefaultMaxStaleAge = TimeSpan.FromHours(24);

    public CachedRateStrategy(ICurrencyConversionStrategy inner, ExchangeRateCache cache, IClock clock, TimeSpan timeToLive, TimeSpan? maxStaleAge = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (timeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "Cache time-to-live must be positive.");
        _timeToLive = timeToLive;
        _maxStaleAge = maxStaleAge ?? DefaultMaxStaleAge;
        if (_maxStaleAge < _timeToLive)
            throw new ArgumentOutOfRangeException(nameof(maxStaleAge), "The stale limit can't be shorter than the cache time-to-live.");
    }

    public string Name => $"{_inner.Name} + cache";

    public async Task<ExchangeRateQuote> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        var from = CurrencyCalculator.NormaliseCurrencyCode(fromCurrency);
        var to = CurrencyCalculator.NormaliseCurrencyCode(toCurrency);
        var now = _clock.UtcNow;

        var hasCached = _cache.TryGet(from, to, out var cached, out var cachedAt);
        if (hasCached && now - cachedAt < _timeToLive)
        {
            return cached with { Source = $"{cached.Source} (cached)", IsStale = false };
        }

        try
        {
            var fresh = await _inner.GetRateAsync(from, to, ct).ConfigureAwait(false);
            _cache.Set(fresh, now);
            return fresh;
        }
        catch (CurrencyConversionException) when (hasCached && now - cachedAt <= _maxStaleAge)
        {
            // Live source failed but we have a recent previous rate: degrade gracefully.
            return cached with { Source = $"{cached.Source} (last known rate, live service unavailable)", IsStale = true };
        }
        catch (CurrencyConversionException ex) when (hasCached)
        {
            // The only rate we have is too old to charge a client with: stop rather than guess.
            throw new CurrencyConversionException(
                $"The live exchange-rate service is unavailable and the last known rate is older than {_maxStaleAge.TotalHours:0} hours.", ex);
        }
    }
}
