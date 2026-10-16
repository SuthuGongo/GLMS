using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Strategy;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Financial;

public class CachedRateStrategyTests
{
    private readonly FakeClock _clock = new();
    private readonly ExchangeRateCache _cache = new();

    private CachedRateStrategy Create(StubRateStrategy inner) => new(inner, _cache, _clock, TimeSpan.FromMinutes(60));

    [Fact]
    public async Task SecondCallWithinTtl_IsServedFromCache()
    {
        var inner = new StubRateStrategy(18.40m);
        var strategy = Create(inner);

        var first = await strategy.GetRateAsync("USD", "ZAR");
        _clock.Advance(TimeSpan.FromMinutes(30));
        var second = await strategy.GetRateAsync("USD", "ZAR");

        Assert.Equal(1, inner.Calls);
        Assert.Equal(first.Rate, second.Rate);
        Assert.Contains("cached", second.Source);
    }

    [Fact]
    public async Task AfterTtlExpires_FetchesFreshRate()
    {
        var inner = new StubRateStrategy().ThenReturns(18.40m).ThenReturns(18.90m);
        var strategy = Create(inner);

        await strategy.GetRateAsync("USD", "ZAR");
        _clock.Advance(TimeSpan.FromMinutes(61));
        var refreshed = await strategy.GetRateAsync("USD", "ZAR");

        Assert.Equal(2, inner.Calls);
        Assert.Equal(18.90m, refreshed.Rate);
    }

    [Fact]
    public async Task LiveServiceDown_FallsBackToLastKnownRate_FlaggedAsStale()
    {
        var inner = new StubRateStrategy().ThenReturns(18.40m).ThenFails();
        var strategy = Create(inner);

        await strategy.GetRateAsync("USD", "ZAR");
        _clock.Advance(TimeSpan.FromHours(2));
        var fallback = await strategy.GetRateAsync("USD", "ZAR");

        Assert.Equal(18.40m, fallback.Rate);
        Assert.True(fallback.IsStale);
    }

    [Fact]
    public async Task LiveServiceDown_AndNothingCached_Throws()
    {
        var strategy = Create(new StubRateStrategy().ThenFails());

        await Assert.ThrowsAsync<CurrencyConversionException>(() => strategy.GetRateAsync("USD", "ZAR"));
    }

    [Fact]
    public async Task CacheIsSharedBetweenStrategyInstances()
    {
        // The cache is a singleton in the web app; each HTTP request gets its own strategy instance.
        var inner = new StubRateStrategy(18.40m);
        await Create(inner).GetRateAsync("USD", "ZAR");
        await Create(inner).GetRateAsync("USD", "ZAR");

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void ZeroTtl_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CachedRateStrategy(new StubRateStrategy(), _cache, _clock, TimeSpan.Zero));
    }

    [Fact]
    public async Task LiveServiceDown_AndLastRateOlderThan24Hours_RefusesToGuess()
    {
        var inner = new StubRateStrategy().ThenReturns(18.40m).ThenFails();
        var strategy = Create(inner);

        await strategy.GetRateAsync("USD", "ZAR");
        _clock.Advance(TimeSpan.FromHours(25));

        var ex = await Assert.ThrowsAsync<CurrencyConversionException>(() => strategy.GetRateAsync("USD", "ZAR"));
        Assert.Contains("older than 24 hours", ex.Message);
    }

    [Fact]
    public void StaleLimitShorterThanCacheLifetime_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CachedRateStrategy(new StubRateStrategy(), _cache, _clock, TimeSpan.FromHours(2), TimeSpan.FromHours(1)));
    }
}
