using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Tests.TestDoubles;
using Moq;

namespace GLMS.Tests.Services;

public class ServiceRequestServiceTests
{
    private readonly FakeClock _clock = new();
    private readonly Mock<IServiceRequestRepository> _requests = new();
    private readonly Mock<IContractRepository> _contracts = new();
    private readonly Mock<ICurrencyConversionStrategy> _rates = new();

    public ServiceRequestServiceTests()
    {
        // The repository hands back the rate row to use (here: the one it was given).
        _requests.Setup(r => r.GetOrAddExchangeRateAsync(It.IsAny<ExchangeRate>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ExchangeRate rate, CancellationToken _) => rate);
    }

    private ServiceRequestService CreateService()
    {
        var validator = new ServiceRequestValidator(_clock);
        var resolver = new ServiceRequestFactoryResolver(new ServiceRequestFactory[]
        {
            new StandardSLAFactory(validator, _clock),
            new PriorityLogisticsFactory(validator, _clock)
        });
        return new ServiceRequestService(_requests.Object, _contracts.Object, validator,
            new FinancialService(_rates.Object), resolver, _clock);
    }

    private void GivenContract(Contract contract) =>
        _contracts.Setup(r => r.GetByIdAsync(contract.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contract);

    private void GivenRate(decimal rate) =>
        _rates.Setup(s => s.GetRateAsync("USD", "ZAR", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ExchangeRateQuote("USD", "ZAR", rate, _clock.UtcNow, "Mock API"));

    [Fact]
    public async Task Create_OnActiveContract_ConvertsUsdToZar_AndSaves()
    {
        GivenContract(TestData.Contract(ContractStatus.Active, ServiceLevel.PriorityLogistics, id: 5));
        GivenRate(18.4523m);

        var request = await CreateService().CreateAsync(new NewServiceRequest(5, "Customs clearance", 1000m));

        Assert.Equal(1000m, request.CostUsd);
        Assert.Equal(18452.30m, request.CostZar);
        Assert.Equal(18.4523m, request.ExchangeRateUsed);
        Assert.Equal(RequestPriority.High, request.Priority);
        _requests.Verify(r => r.AddAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        _requests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_StoresTheRateOnce_AndLinksTheRequestToIt()
    {
        GivenContract(TestData.Contract(ContractStatus.Active, id: 5));
        GivenRate(18.4523m);
        var storedRate = new ExchangeRate { Id = 42, BaseCurrency = "USD", QuoteCurrency = "ZAR", Rate = 18.4523m, Source = "Stored" };
        _requests.Setup(r => r.GetOrAddExchangeRateAsync(It.IsAny<ExchangeRate>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(storedRate);

        var request = await CreateService().CreateAsync(new NewServiceRequest(5, "Customs clearance", 100m));

        Assert.Same(storedRate, request.ExchangeRate);   // existing row reused, no duplicate rate
        _requests.Verify(r => r.GetOrAddExchangeRateAsync(
            It.Is<ExchangeRate>(x => x.BaseCurrency == "USD" && x.QuoteCurrency == "ZAR" && x.Rate == 18.4523m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ContractStatus.Expired)]
    [InlineData(ContractStatus.OnHold)]
    public async Task Create_OnExpiredOrOnHoldContract_IsBlocked_NothingSaved_NoApiCall(ContractStatus status)
    {
        GivenContract(TestData.Contract(status, id: 5));
        GivenRate(18.50m);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().CreateAsync(new NewServiceRequest(5, "Work", 100m)));

        _requests.Verify(r => r.AddAsync(It.IsAny<ServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _requests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _rates.Verify(s => s.GetRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ContractDoesNotExist_IsBlocked()
    {
        _contracts.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((Contract?)null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().CreateAsync(new NewServiceRequest(99, "Work", 100m)));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task Create_WhenCurrencyApiIsDown_DoesNotSaveRequest()
    {
        GivenContract(TestData.Contract(ContractStatus.Active, id: 5));
        _rates.Setup(s => s.GetRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new CurrencyConversionException("API down"));

        await Assert.ThrowsAsync<CurrencyConversionException>(() =>
            CreateService().CreateAsync(new NewServiceRequest(5, "Work", 100m)));

        _requests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ZeroCost_IsRejected_BeforeCallingApi()
    {
        GivenContract(TestData.Contract(ContractStatus.Active, id: 5));
        GivenRate(18.50m);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService().CreateAsync(new NewServiceRequest(5, "Work", 0m)));

        _rates.Verify(s => s.GetRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NullInput_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateService().CreateAsync(null!));
    }
}
