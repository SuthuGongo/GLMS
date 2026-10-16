using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Factory;

/// <summary>Factory Method pattern: each SLA tier's factory applies its own rules; validation can't be skipped.</summary>
public class ServiceRequestFactoryTests
{
    private readonly FakeClock _clock = new();
    private readonly ServiceRequestValidator _validator;
    private readonly StandardSLAFactory _standard;
    private readonly PriorityLogisticsFactory _priority;

    public ServiceRequestFactoryTests()
    {
        _validator = new ServiceRequestValidator(_clock);
        _standard = new StandardSLAFactory(_validator, _clock);
        _priority = new PriorityLogisticsFactory(_validator, _clock);
    }

    [Fact]
    public void StandardFactory_CreatesNormalPriority_With48HourResponse()
    {
        var contract = TestData.Contract(level: ServiceLevel.Standard, id: 7);
        var input = new NewServiceRequest(7, "  Pallet storage  ", 200m);

        var request = _standard.CreateRequest(contract, input, TestData.Conversion(200m, 18.50m));

        Assert.Equal(7, request.ContractId);
        Assert.Equal("Pallet storage", request.Description);
        Assert.Equal(RequestPriority.Normal, request.Priority);
        Assert.Equal(_clock.UtcNow.AddHours(48), request.ResponseDueUtc);
        Assert.Equal(ServiceRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Factory_StoresUsdAmount_RateUsed_AndCalculatedZar()
    {
        var request = _standard.CreateRequest(TestData.Contract(), new NewServiceRequest(1, "Work", 1250.75m), TestData.Conversion(1250.75m, 18.2m));

        Assert.Equal(1250.75m, request.CostUsd);
        Assert.Equal(18.2m, request.ExchangeRateUsed);
        Assert.Equal(22763.65m, request.CostZar);
    }

    [Fact]
    public void StandardFactory_RejectsUrgentRequests()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            _standard.CreateRequest(TestData.Contract(), new NewServiceRequest(1, "Work", 10m, IsUrgent: true), TestData.Conversion(10m)));
        Assert.Contains("Priority Logistics", ex.Message);
    }

    [Fact]
    public void PriorityFactory_CreatesHighPriority_With8HourResponse()
    {
        var contract = TestData.Contract(level: ServiceLevel.PriorityLogistics);

        var request = _priority.CreateRequest(contract, new NewServiceRequest(1, "Work", 10m), TestData.Conversion(10m));

        Assert.Equal(RequestPriority.High, request.Priority);
        Assert.Equal(_clock.UtcNow.AddHours(8), request.ResponseDueUtc);
    }

    [Fact]
    public void PriorityFactory_UrgentRequest_EscalatesToCritical_With2HourResponse()
    {
        var contract = TestData.Contract(level: ServiceLevel.PriorityLogistics);

        var request = _priority.CreateRequest(contract, new NewServiceRequest(1, "Work", 10m, IsUrgent: true), TestData.Conversion(10m));

        Assert.Equal(RequestPriority.Critical, request.Priority);
        Assert.Equal(_clock.UtcNow.AddHours(2), request.ResponseDueUtc);
    }

    [Theory]
    [InlineData(ContractStatus.Expired)]
    [InlineData(ContractStatus.OnHold)]
    [InlineData(ContractStatus.Draft)]
    public void Factory_CannotBeUsedToBypassValidation(ContractStatus status)
    {
        // Even when a caller goes straight to the factory, the workflow rule is enforced.
        var contract = TestData.Contract(status);
        Assert.Throws<BusinessRuleException>(() =>
            _standard.CreateRequest(contract, new NewServiceRequest(1, "Work", 10m), TestData.Conversion(10m)));
    }

    [Fact]
    public void Factory_WrongTierForContract_Throws()
    {
        var priorityContract = TestData.Contract(level: ServiceLevel.PriorityLogistics);
        Assert.Throws<InvalidOperationException>(() =>
            _standard.CreateRequest(priorityContract, new NewServiceRequest(1, "Work", 10m), TestData.Conversion(10m)));
    }

    [Fact]
    public void Factory_ConversionForDifferentAmount_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _standard.CreateRequest(TestData.Contract(), new NewServiceRequest(1, "Work", 10m), TestData.Conversion(99m)));
    }

    // ------------------------------------------------------------------ Resolver

    [Theory]
    [InlineData(ServiceLevel.Standard, typeof(StandardSLAFactory))]
    [InlineData(ServiceLevel.PriorityLogistics, typeof(PriorityLogisticsFactory))]
    public void Resolver_PicksFactoryForContractTier(ServiceLevel level, Type expected)
    {
        var resolver = new ServiceRequestFactoryResolver(new ServiceRequestFactory[] { _standard, _priority });
        Assert.IsType(expected, resolver.Resolve(level));
    }

    [Fact]
    public void Resolver_UnregisteredTier_Throws()
    {
        var resolver = new ServiceRequestFactoryResolver(new ServiceRequestFactory[] { _standard });
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ServiceLevel.PriorityLogistics));
    }

    [Fact]
    public void Resolver_DuplicateRegistration_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceRequestFactoryResolver(new ServiceRequestFactory[] { _standard, new StandardSLAFactory(_validator, _clock) }));
    }
}
