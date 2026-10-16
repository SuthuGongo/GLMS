using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Services;

/// <summary>
/// The rules that keep the normalised schema consistent: one agreement row per contract,
/// one row per published exchange rate, unique client names and emails the database will accept.
/// </summary>
public class NormalisationRulesTests
{
    private readonly FakeClock _clock = new();

    // ------------------------------------------------------------------ Signed agreement (ContractAgreements table)

    [Fact]
    public void ReplacingAgreement_UpdatesTheSameRecord_InsteadOfAddingAnother()
    {
        var contract = TestData.Contract(ContractStatus.Draft, withAgreement: false);
        contract.AttachSignedAgreement("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.pdf", "v1.pdf", TestData.Today, 100);
        var first = contract.SignedAgreement;

        contract.AttachSignedAgreement("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.pdf", "v2.pdf", TestData.Today.AddDays(1), 200);

        Assert.Same(first, contract.SignedAgreement);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.pdf", contract.SignedAgreementFileName);
        Assert.Equal("v2.pdf", contract.SignedAgreementOriginalName);
        Assert.Equal(200, contract.SignedAgreement!.SizeBytes);
    }

    [Fact]
    public void ContractWithoutAgreement_HasNoAgreementRecord()
    {
        var contract = TestData.Contract(ContractStatus.Draft, withAgreement: false);

        Assert.Null(contract.SignedAgreement);
        Assert.False(contract.HasSignedAgreement);
        Assert.Null(contract.SignedAgreementFileName);
    }

    [Fact]
    public void Agreement_NegativeSize_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignedAgreement("a.pdf", "a.pdf", -1, TestData.Today));
    }

    // ------------------------------------------------------------------ Exchange rates (ExchangeRates table)

    [Fact]
    public void ToStoredPrecision_DropsFractionsOfASecond_AndKeepsKind()
    {
        var value = new DateTime(2026, 10, 13, 2, 0, 5, DateTimeKind.Utc).AddTicks(4_567_891);

        var stored = ExchangeRate.ToStoredPrecision(value);

        Assert.Equal(new DateTime(2026, 10, 13, 2, 0, 5, DateTimeKind.Utc), stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }

    [Theory]
    [InlineData("USD", "ZAR", "18.4523", 0, true)]
    [InlineData("usd", "zar", "18.4523", 0, true)]   // currency codes compare case-insensitively
    [InlineData("USD", "ZAR", "18.4524", 0, false)]  // different rate
    [InlineData("USD", "ZAR", "18.4523", 60, false)] // published at a different time
    [InlineData("EUR", "ZAR", "18.4523", 0, false)]  // different currency pair
    public void IsSameRateAs_ComparesTheNaturalKey(string from, string to, string rate, int secondsLater, bool expected)
    {
        var published = new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc);
        var stored = new ExchangeRate { BaseCurrency = "USD", QuoteCurrency = "ZAR", Rate = 18.4523m, PublishedUtc = published };
        var candidate = new ExchangeRate
        {
            BaseCurrency = from, QuoteCurrency = to,
            Rate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture),
            PublishedUtc = published.AddSeconds(secondsLater)
        };

        Assert.Equal(expected, stored.IsSameRateAs(candidate));
    }

    [Fact]
    public void Factory_BuildsRateRecord_FromTheConversion()
    {
        var validator = new ServiceRequestValidator(_clock);
        var conversion = new CurrencyConversion("USD", "ZAR", 100m, 18.4523m, 1845.23m, "ExchangeRate-API (live)",
            new DateTime(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc).AddMilliseconds(750), false);

        var request = new StandardSLAFactory(validator, _clock)
            .CreateRequest(TestData.Contract(), new NewServiceRequest(1, "Work", 100m), conversion);

        Assert.NotNull(request.ExchangeRate);
        Assert.Equal("USD", request.ExchangeRate!.BaseCurrency);
        Assert.Equal("ZAR", request.ExchangeRate.QuoteCurrency);
        Assert.Equal(18.4523m, request.ExchangeRateUsed);
        Assert.Equal(0, request.ExchangeRate.PublishedUtc.Millisecond); // stored to the whole second
        Assert.Equal(1845.23m, request.CostZar);
    }

    [Fact]
    public async Task TwoRequestsCostedWithTheSameRate_ShareOneRateRecord()
    {
        var contracts = new FakeContractRepository();
        var requests = new FakeServiceRequestRepository();
        contracts.Contracts.Add(TestData.Contract(ContractStatus.Active, id: 1));
        var validator = new ServiceRequestValidator(_clock);
        var service = new ServiceRequestService(requests, contracts, validator,
            new FinancialService(new StubRateStrategy(18.4523m)),
            new ServiceRequestFactoryResolver(new ServiceRequestFactory[] { new StandardSLAFactory(validator, _clock) }),
            _clock);

        var first = await service.CreateAsync(new NewServiceRequest(1, "First job", 100m));
        var second = await service.CreateAsync(new NewServiceRequest(1, "Second job", 250m));

        Assert.Single(requests.Rates);
        Assert.Same(first.ExchangeRate, second.ExchangeRate);
        Assert.Equal(1845.23m, first.CostZar);
        Assert.Equal(4613.08m, second.CostZar); // 250 x 18.4523 = 4613.075 -> 4613.08
    }

    [Fact]
    public async Task ANewPublishedRate_GetsItsOwnRecord()
    {
        var contracts = new FakeContractRepository();
        var requests = new FakeServiceRequestRepository();
        contracts.Contracts.Add(TestData.Contract(ContractStatus.Active, id: 1));
        var validator = new ServiceRequestValidator(_clock);
        var service = new ServiceRequestService(requests, contracts, validator,
            new FinancialService(new StubRateStrategy().ThenReturns(18.40m).ThenReturns(18.90m)),
            new ServiceRequestFactoryResolver(new ServiceRequestFactory[] { new StandardSLAFactory(validator, _clock) }),
            _clock);

        await service.CreateAsync(new NewServiceRequest(1, "First job", 100m));
        await service.CreateAsync(new NewServiceRequest(1, "Second job", 100m));

        Assert.Equal(2, requests.Rates.Count);
    }

    // ------------------------------------------------------------------ Clients (unique name, email format)

    [Fact]
    public async Task CreatingAClientWithAnExistingName_IsRejected_IgnoringCase()
    {
        var repo = new FakeClientRepository();
        var service = new ClientService(repo, _clock);
        await service.CreateAsync(new ClientInput("Atlantic Freight", "", "a@atlantic.co.za", "", "Southern Africa"));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateAsync(new ClientInput("atlantic freight", "", "b@atlantic.co.za", "", "Southern Africa")));

        Assert.Contains("already exists", ex.Message);
        Assert.Single(repo.Clients);
    }

    [Fact]
    public async Task UpdatingAClient_KeepingItsOwnName_IsAllowed()
    {
        var repo = new FakeClientRepository();
        var service = new ClientService(repo, _clock);
        var client = await service.CreateAsync(new ClientInput("Atlantic Freight", "", "a@atlantic.co.za", "", "Southern Africa"));

        var updated = await service.UpdateAsync(client.Id, new ClientInput("Atlantic Freight", "Thandi", "a@atlantic.co.za", "021 555 0101", "Southern Africa"));

        Assert.Equal("Thandi", updated.ContactPerson);
    }

    [Theory]
    [InlineData("thandi@atlanticfreight.co.za", true)]
    [InlineData("l.becker@rhinevalley.de", true)]
    [InlineData("a@b", false)]          // no dot after @: the database CHECK constraint would reject it
    [InlineData("no-at-sign.com", false)]
    [InlineData("two words@x.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EmailRule_MatchesTheDatabaseConstraint(string? email, bool expected)
    {
        Assert.Equal(expected, ClientService.IsValidEmail(email));
    }
}
