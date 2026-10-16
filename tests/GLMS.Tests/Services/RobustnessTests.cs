using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Files;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Core.Validation;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Services;
public class FixedRateStrategyTests
{
    private readonly FakeClock _clock = new();

    private FixedRateStrategy Create() =>
        new(new Dictionary<string, decimal> { ["USD-ZAR"] = 18.50m, ["eur-zar"] = 20.10m }, _clock);

    [Fact]
    public async Task ReturnsTheConfiguredRate_ForAnyCaseOfCurrencyCode()
    {
        var quote = await Create().GetRateAsync("usd", "ZAR");

        Assert.Equal(18.50m, quote.Rate);
        Assert.Equal("USD", quote.FromCurrency);
        Assert.Equal(_clock.UtcNow.Date, quote.RetrievedUtc); 
    }

    [Fact]
    public async Task UnconfiguredPair_Throws()
    {
        await Assert.ThrowsAsync<CurrencyConversionException>(() => Create().GetRateAsync("GBP", "ZAR"));
    }

    [Fact]
    public async Task PlugsIntoFinancialService_WithoutAnyOtherChange()
    {
        var service = new FinancialService(Create());

        var result = await service.ConvertUsdToZarAsync(100m);

        Assert.Equal(1850.00m, result.ConvertedAmount);
        Assert.Equal("Fixed rate (appsettings.json)", service.CurrentStrategyName);
    }

    [Theory]
    [InlineData("USDZAR", "18.5")]   // not a pair
    [InlineData("USD-ZAR", "0")]     // zero rate
    [InlineData("USD-ZAR", "-1")]    // negative rate
    [InlineData("US-ZAR", "18.5")]   // bad currency code
    public void BadConfiguration_IsRejectedAtStartUp(string pair, string rate)
    {
        var rates = new Dictionary<string, decimal> { [pair] = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) };
        Assert.ThrowsAny<ArgumentException>(() => new FixedRateStrategy(rates, _clock));
    }

    [Fact]
    public void EmptyConfiguration_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new FixedRateStrategy(new Dictionary<string, decimal>(), _clock));
    }
}

public class RatePrecisionTests
{
    [Theory]
    [InlineData("18.4523", "18.4523")]
    [InlineData("18.45234567", "18.452346")]   
    [InlineData("18.4523445", "18.452345")]    
    public void NormaliseRate_RoundsToSixDecimals(string raw, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            CurrencyCalculator.NormaliseRate(decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void NormaliseRate_TooSmallToStore_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyCalculator.NormaliseRate(0.0000001m));
    }

    [Fact]
    public async Task FinancialService_UsesTheStoredPrecisionRate_SoStoredRateTimesUsdEqualsStoredZar()
    {
        var service = new FinancialService(new StubRateStrategy(18.45234567m));

        var result = await service.ConvertUsdToZarAsync(1000m);

        Assert.Equal(18.452346m, result.Rate);
        Assert.Equal(CurrencyCalculator.Convert(1000m, 18.452346m), result.ConvertedAmount);
        Assert.Equal(18452.35m, result.ConvertedAmount);
    }
}

public class ContractValidatorTests
{
    private static readonly DateTime Today = new(2026, 6, 15);

    private static ContractInput Input(string title = "Ocean freight", int clientId = 1, int startOffset = 0, int endOffset = 365, ServiceLevel level = ServiceLevel.Standard) =>
        new(clientId, title, Today.AddDays(startOffset), Today.AddDays(endOffset), level);

    [Fact]
    public void ValidInput_HasNoErrors() => Assert.Empty(ContractValidator.Validate(Input()));

    [Fact]
    public void NullInput_ReportsAnError_InsteadOfCrashing() => Assert.Single(ContractValidator.Validate(null));

    [Theory]
    [InlineData(0, "end date must be after")]       // same day
    [InlineData(-1, "end date must be after")]      // before
    [InlineData(3654, "longer than 10 years")]      // one day over 10 years (10 years from Jun 2026 = 3653 days, 3 leap years)
    public void BadTerm_IsRejected(int endOffset, string expected)
    {
        var error = Assert.Single(ContractValidator.Validate(Input(endOffset: endOffset)));
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactlyTenYears_IsAllowed()
    {
        Assert.Empty(ContractValidator.Validate(new ContractInput(1, "T", Today, Today.AddYears(10), ServiceLevel.Standard)));
    }

    [Fact]
    public void MissingDates_AreReported()
    {
        Assert.NotEmpty(ContractValidator.Validate(new ContractInput(1, "T", default, default, ServiceLevel.Standard)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitle_IsRejected(string title) => Assert.Single(ContractValidator.Validate(Input(title: title)));

    [Fact]
    public void TitleOverDatabaseLimit_IsRejected()
    {
        Assert.Single(ContractValidator.Validate(Input(title: new string('x', ContractValidator.TitleMaxLength + 1))));
    }

    [Fact]
    public void NoClientAndUnknownServiceLevel_AreBothReported()
    {
        Assert.Equal(2, ContractValidator.Validate(Input(clientId: 0, level: (ServiceLevel)99)).Count);
    }

    [Fact]
    public void Activation_NeedsAgreement_AndAFutureEndDate()
    {
        var expired = new ContractInput(1, "T", Today.AddYears(-1), Today.AddDays(-1), ServiceLevel.Standard);
        Assert.Equal(2, ContractValidator.ValidateActivation(expired, hasSignedAgreement: false, Today).Count);
        Assert.Empty(ContractValidator.ValidateActivation(Input(), hasSignedAgreement: true, Today));
    }

    [Fact]
    public void TamperedStatusValue_IsRejected()
    {
        Assert.NotEmpty(ContractValidator.ValidateStatus((ContractStatus)42));
        Assert.Empty(ContractValidator.ValidateStatus(ContractStatus.OnHold));
    }
}

public class ClientValidatorTests
{
    private static ClientInput Input(string name = "Atlantic Freight", string email = "a@atlantic.co.za", string region = "Southern Africa",
        string contact = "Thandi", string phone = "+27 21 555 0101") => new(name, contact, email, phone, region);

    [Fact]
    public void ValidInput_HasNoErrors() => Assert.Empty(ClientValidator.Validate(Input()));

    [Fact]
    public void NullInput_ReportsAnError() => Assert.Single(ClientValidator.Validate(null));

    [Fact]
    public void EveryFieldOverItsDatabaseLimit_IsReported()
    {
        var errors = ClientValidator.Validate(Input(
            name: new string('n', ClientValidator.NameMaxLength + 1),
            region: new string('r', ClientValidator.RegionMaxLength + 1),
            contact: new string('c', ClientValidator.ContactMaxLength + 1),
            phone: new string('1', ClientValidator.PhoneMaxLength + 1)));

        Assert.True(errors.Count >= 4);
    }

    [Theory]
    [InlineData("021 555 0101")]
    [InlineData("+27 (21) 555-0101")]
    [InlineData("")]                 // optional
    public void AcceptablePhoneNumbers(string phone) => Assert.Empty(ClientValidator.Validate(Input(phone: phone)));

    [Theory]
    [InlineData("call me")]
    [InlineData("12")]
    [InlineData("021#555")]
    public void BadPhoneNumbers_AreRejected(string phone) => Assert.NotEmpty(ClientValidator.Validate(Input(phone: phone)));
}

public class ConcurrencyTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeContractRepository _contracts = new();
    private readonly ContractService _service;

    public ConcurrencyTests()
    {
        var clients = new FakeClientRepository();
        clients.Clients.Add(new Client { Id = 1, Name = "Atlantic Freight", Email = "a@b.com", Region = "Africa" });
        _service = new ContractService(_contracts, clients, new FakeFileStorage(),
            new IContractObserver[] { new ServiceRequestValidator(_clock) }, _clock);
        _contracts.Contracts.Add(TestData.Contract(ContractStatus.Active, id: 1));
    }

    private ContractInput Edit(string title) => new(1, title, _clock.Today.AddMonths(-1), _clock.Today.AddMonths(11), ServiceLevel.Standard);

    [Fact]
    public async Task SaveWithTheVersionTheUserOpened_Succeeds()
    {
        await _contracts.SaveChangesAsync();                 // give the contract a version
        var opened = _contracts.Contracts[0].RowVersion!.ToArray();

        var updated = await _service.UpdateAsync(1, Edit("Renamed"), opened);

        Assert.Equal("Renamed", updated.Title);
    }

    [Fact]
    public async Task SaveAfterSomeoneElseChangedIt_IsRejected_WithAClearMessage()
    {
        await _contracts.SaveChangesAsync();
        var opened = _contracts.Contracts[0].RowVersion!.ToArray();
        await _service.UpdateAsync(1, Edit("Changed by colleague"), opened);   // someone else saves first

        var ex = await Assert.ThrowsAsync<ConcurrencyException>(() => _service.UpdateAsync(1, Edit("My stale edit"), opened));

        Assert.Contains("Someone else changed", ex.Message);
    }

    [Fact]
    public async Task StaleStatusChange_IsRejected()
    {
        await _contracts.SaveChangesAsync();
        var opened = _contracts.Contracts[0].RowVersion!.ToArray();
        await _service.ChangeStatusAsync(1, ContractStatus.OnHold, opened);

        await Assert.ThrowsAsync<ConcurrencyException>(() => _service.ChangeStatusAsync(1, ContractStatus.Expired, opened));
    }

    [Fact]
    public async Task TamperedStatus_IsRejected_BeforeTouchingTheContract()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.ChangeStatusAsync(1, (ContractStatus)99));
        Assert.Equal(ContractStatus.Active, _contracts.Contracts[0].Status);
    }
}

public class CompletePdfTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "glms-pdf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Trailer_Present_IsAccepted() =>
        Assert.Null(Record.Exception(() => FileValidator.ValidatePdfTrailer(TestData.PdfBytes())));

    [Fact]
    public void Trailer_Missing_IsRejected() =>
        Assert.Throws<InvalidFileException>(() => FileValidator.ValidatePdfTrailer(TestData.TruncatedPdfBytes()));

    [Fact]
    public async Task Storage_RejectsATruncatedPdf_AndLeavesNoFileBehind()
    {
        var storage = new LocalFileStorageService(_root);
        var bytes = TestData.TruncatedPdfBytes(2048);

        await Assert.ThrowsAsync<InvalidFileException>(() => storage.SaveAsync(new MemoryStream(bytes), "cut-off.pdf", bytes.Length));

        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Storage_AcceptsAPdfWithWhitespaceAfterTheEndMarker()
    {
        var storage = new LocalFileStorageService(_root);
        var bytes = TestData.PdfBytes(4096).Concat(System.Text.Encoding.ASCII.GetBytes("\r\n  \r\n")).ToArray();

        var stored = await storage.SaveAsync(new MemoryStream(bytes), "agreement.pdf", bytes.Length);

        Assert.True(storage.Exists(stored.StoredFileName));
    }

    [Fact]
    public async Task Storage_RealGeneratedPdf_IsAccepted()
    {
        // A complete one-page PDF, built the same way as the app's seed data.
        var pdf = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n" +
            "xref\n0 3\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n107\n%%EOF\n");
        var storage = new LocalFileStorageService(_root);

        var stored = await storage.SaveAsync(new MemoryStream(pdf), "seed.pdf", pdf.Length);

        Assert.Equal(pdf.Length, stored.SizeBytes);
    }
}
