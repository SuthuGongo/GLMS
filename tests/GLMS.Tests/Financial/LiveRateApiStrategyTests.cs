using System.Net;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Strategy;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Financial;
public class LiveRateApiStrategyTests
{
    private static LiveRateApiStrategy Create(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://open.er-api.com/v6/latest/") }, retryDelay: TimeSpan.Zero);

    [Fact]
    public async Task GetRate_ParsesUsdToZarFromApiResponse()
    {
        var handler = FakeHttpMessageHandler.Json(TestData.RatesJson);

        var quote = await Create(handler).GetRateAsync("USD", "ZAR");

        Assert.Equal(18.4523m, quote.Rate);
        Assert.Equal("USD", quote.FromCurrency);
        Assert.Equal("ZAR", quote.ToCurrency);
        Assert.False(quote.IsStale);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1781481600).UtcDateTime, quote.RetrievedUtc);
    }

    [Fact]
    public async Task GetRate_CallsTheBaseCurrencyEndpoint()
    {
        var handler = FakeHttpMessageHandler.Json(TestData.RatesJson);

        await Create(handler).GetRateAsync("usd", "zar");

        Assert.Equal("https://open.er-api.com/v6/latest/USD", Assert.Single(handler.RequestedUris).ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetRate_HttpError_ThrowsCurrencyConversionException(HttpStatusCode status)
    {
        var handler = FakeHttpMessageHandler.Json("{}", status);

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
    }

    [Fact]
    public async Task GetRate_NetworkDown_ThrowsCurrencyConversionException()
    {
        var handler = FakeHttpMessageHandler.Throws(new HttpRequestException("No such host is known"));

        var ex = await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task GetRate_Timeout_ThrowsCurrencyConversionException()
    {
        var handler = FakeHttpMessageHandler.Throws(new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
    }

    [Fact]
    public async Task GetRate_ApiReportsError_Throws()
    {
        var handler = FakeHttpMessageHandler.Json("""{ "result": "error", "error-type": "unsupported-code" }""");

        var ex = await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
        Assert.Contains("unsupported-code", ex.Message);
    }

    [Fact]
    public async Task GetRate_TargetCurrencyMissing_Throws()
    {
        var handler = FakeHttpMessageHandler.Json("""{ "result": "success", "rates": { "EUR": 0.92 } }""");

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
    }

    [Fact]
    public async Task GetRate_MalformedJson_Throws()
    {
        var handler = FakeHttpMessageHandler.Json("<html>Gateway error</html>");

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
    }

    [Fact]
    public async Task GetRate_ZeroRate_Throws()
    {
        var handler = FakeHttpMessageHandler.Json("""{ "result": "success", "rates": { "ZAR": 0 } }""");

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
    }

    [Fact]
    public async Task GetRate_InvalidCurrencyCode_ThrowsBeforeAnyHttpCall()
    {
        var handler = FakeHttpMessageHandler.Json(TestData.RatesJson);

        await Assert.ThrowsAsync<ArgumentException>(() => Create(handler).GetRateAsync("DOLLARS", "ZAR"));
        Assert.Empty(handler.RequestedUris);
    }

    // ------------------------------------------------------------------ Retry on transient failures

    private static FakeHttpMessageHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var queue = new Queue<Func<HttpResponseMessage>>(responses);
        return new FakeHttpMessageHandler(_ => queue.Count > 1 ? queue.Dequeue()() : queue.Peek()());
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{}") };
    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent(TestData.RatesJson) };

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task TransientFailure_IsRetriedOnce_ThenSucceeds(HttpStatusCode firstResponse)
    {
        var handler = Sequence(() => Status(firstResponse), Ok);
        var strategy = Create(handler);

        var quote = await strategy.GetRateAsync("USD", "ZAR");

        Assert.Equal(18.4523m, quote.Rate);
        Assert.Equal(2, handler.RequestedUris.Count);
        Assert.Equal(2, strategy.LastAttemptCount);
    }

    [Fact]
    public async Task NetworkBlip_IsRetriedOnce_ThenSucceeds()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler(_ => ++calls == 1 ? throw new HttpRequestException("blip") : Ok());

        var quote = await Create(handler).GetRateAsync("USD", "ZAR");

        Assert.Equal(18.4523m, quote.Rate);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task PermanentFailureOrRateLimit_IsNotRetried(HttpStatusCode status)
    {
        var handler = FakeHttpMessageHandler.Json("{}", status);

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
        Assert.Single(handler.RequestedUris);
    }

    [Fact]
    public async Task ServiceStaysDown_StopsAfterTwoAttempts()
    {
        var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create(handler).GetRateAsync("USD", "ZAR"));
        Assert.Equal(2, handler.RequestedUris.Count);
    }

    [Fact]
    public void ZeroAttempts_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveRateApiStrategy(new HttpClient(), maxAttempts: 0));
    }
}
