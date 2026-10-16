using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Strategy;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Financial;

/// <summary>Strategy pattern: the Context (FinancialService) works with any ICurrencyConversionStrategy.</summary>
public class FinancialServiceTests
{
    [Fact]
    public async Task ConvertUsdToZar_UsesStrategyRate_AndCalculatesLocalCost()
    {
        var service = new FinancialService(new StubRateStrategy(18.50m));

        var result = await service.ConvertUsdToZarAsync(100m);

        Assert.Equal("USD", result.FromCurrency);
        Assert.Equal("ZAR", result.ToCurrency);
        Assert.Equal(18.50m, result.Rate);
        Assert.Equal(1850.00m, result.ConvertedAmount);
        Assert.Equal(100m, result.SourceAmount);
    }

    [Fact]
    public async Task SetStrategy_SwapsProviderAtRuntime_WithoutChangingService()
    {
        var service = new FinancialService(new StubRateStrategy(18m, "First"));
        service.SetStrategy(new StubRateStrategy(20m, "Second"));

        var result = await service.ConvertUsdToZarAsync(10m);

        Assert.Equal("Second", service.CurrentStrategyName);
        Assert.Equal(200m, result.ConvertedAmount);
    }

    [Fact]
    public async Task SameCurrency_ReturnsIdentityRate_WithoutCallingStrategy()
    {
        var stub = new StubRateStrategy();
        var service = new FinancialService(stub);

        var result = await service.ConvertAsync(250m, "ZAR", "zar");

        Assert.Equal(1m, result.Rate);
        Assert.Equal(250m, result.ConvertedAmount);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task StrategyFailure_SurfacesAsCurrencyConversionException()
    {
        var service = new FinancialService(new StubRateStrategy().ThenFails());

        await Assert.ThrowsAsync<CurrencyConversionException>(() => service.ConvertUsdToZarAsync(10m));
    }

    [Fact]
    public async Task NegativeAmount_Throws_BeforeCallingStrategy()
    {
        var stub = new StubRateStrategy();
        var service = new FinancialService(stub);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ConvertUsdToZarAsync(-5m));
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public void NullStrategy_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new FinancialService(null!));
        var service = new FinancialService(new StubRateStrategy());
        Assert.Throws<ArgumentNullException>(() => service.SetStrategy(null!));
    }
}
