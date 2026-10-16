using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Services;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Services;

/// <summary>Bigger system tasks: contract creation with PDF, activation, uploads, automatic expiry.</summary>
public class ContractServiceTests
{
    private readonly FakeClock _clock = new();
    private readonly FakeContractRepository _contracts = new();
    private readonly FakeClientRepository _clients = new();
    private readonly FakeFileStorage _files = new();
    private readonly InMemoryAuditSink _audit = new();
    private readonly ContractService _service;

    public ContractServiceTests()
    {
        _clients.Clients.Add(new Client { Id = 1, Name = "Atlantic Freight", Email = "a@b.com", Region = "Africa" });
        var observers = new IContractObserver[] { new ServiceRequestValidator(_clock), new ComplianceLogger(_audit, _clock) };
        _service = new ContractService(_contracts, _clients, _files, observers, _clock);
    }

    private ContractInput ValidInput(DateTime? start = null, DateTime? end = null) =>
        new(1, "Ocean freight", start ?? _clock.Today, end ?? _clock.Today.AddYears(1), ServiceLevel.Standard);

    private static FileUpload Pdf(string name = "signed.pdf") { var b = TestData.PdfBytes(); return new FileUpload(new MemoryStream(b), name, b.Length); }
    private static FileUpload Exe() { var b = TestData.ExeBytes(); return new FileUpload(new MemoryStream(b), "malware.exe", b.Length); }

    [Fact]
    public async Task Create_WithoutFile_IsSavedAsDraft()
    {
        var contract = await _service.CreateAsync(ValidInput(), null, activate: false);

        Assert.Equal(ContractStatus.Draft, contract.Status);
        Assert.StartsWith("TM-2026-", contract.ContractNumber);
        Assert.Single(_contracts.Contracts);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Create_WithPdfAndActivate_IsActive_FileStored_ActivationAudited()
    {
        var contract = await _service.CreateAsync(ValidInput(), Pdf(), activate: true);

        Assert.Equal(ContractStatus.Active, contract.Status);
        Assert.True(contract.HasSignedAgreement);
        Assert.True(_files.Exists(contract.SignedAgreementFileName!));
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(ContractStatus.Active, entry.ToStatus);
    }

    [Fact]
    public async Task Create_ActivateWithoutFile_IsRejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.CreateAsync(ValidInput(), null, activate: true));
        Assert.Empty(_contracts.Contracts);
    }

    [Fact]
    public async Task Create_WithExeFile_IsRejected_AndNothingSaved()
    {
        await Assert.ThrowsAsync<InvalidFileException>(() => _service.CreateAsync(ValidInput(), Exe(), activate: false));
        Assert.Empty(_contracts.Contracts);
        Assert.Empty(_files.Files);
    }

    [Fact]
    public async Task Create_EndDateBeforeStartDate_IsRejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _service.CreateAsync(ValidInput(_clock.Today, _clock.Today.AddDays(-1)), null, false));
    }

    [Fact]
    public async Task Create_UnknownClient_IsRejected()
    {
        var input = ValidInput() with { ClientId = 999 };
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _service.CreateAsync(input, null, false));
        Assert.Contains("client", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAgreement_ReplacesOldFile_AndDeletesIt()
    {
        var contract = await _service.CreateAsync(ValidInput(), Pdf("v1.pdf"), activate: false);
        var oldFile = contract.SignedAgreementFileName!;

        await _service.UploadSignedAgreementAsync(contract.Id, Pdf("v2.pdf"));

        Assert.NotEqual(oldFile, contract.SignedAgreementFileName);
        Assert.Equal("v2.pdf", contract.SignedAgreementOriginalName);
        Assert.Contains(oldFile, _files.Deleted);
    }

    [Fact]
    public async Task UploadAgreement_Exe_IsRejected_ContractUnchanged()
    {
        var contract = await _service.CreateAsync(ValidInput(), Pdf(), activate: false);
        var before = contract.SignedAgreementFileName;

        await Assert.ThrowsAsync<InvalidFileException>(() => _service.UploadSignedAgreementAsync(contract.Id, Exe()));

        Assert.Equal(before, contract.SignedAgreementFileName);
    }

    [Fact]
    public async Task Download_ReturnsStoredPdf_WithFriendlyName()
    {
        var contract = await _service.CreateAsync(ValidInput(), Pdf("agreement.pdf"), activate: false);

        var download = await _service.DownloadSignedAgreementAsync(contract.Id);

        Assert.EndsWith("_agreement.pdf", download.DownloadName);
        Assert.True(download.Content.Length > 0);
    }

    [Fact]
    public async Task Download_WhenNoAgreement_ThrowsNotFound()
    {
        var contract = await _service.CreateAsync(ValidInput(), null, activate: false);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DownloadSignedAgreementAsync(contract.Id));
    }

    [Fact]
    public async Task ChangeStatus_ReactivatingContractPastEndDate_IsRejected()
    {
        var contract = TestData.Contract(ContractStatus.OnHold, start: _clock.Today.AddYears(-1), end: _clock.Today.AddDays(-1), id: 1);
        _contracts.Contracts.Add(contract);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.ChangeStatusAsync(1, ContractStatus.Active));
        Assert.Equal(ContractStatus.OnHold, contract.Status);
    }

    [Fact]
    public async Task ChangeStatus_PutOnHold_NotifiesObservers_HoldsRequests_AndAudits()
    {
        var contract = TestData.Contract(ContractStatus.Active, id: 1);
        var request = TestData.Request(contract);
        _contracts.Contracts.Add(contract);

        await _service.ChangeStatusAsync(1, ContractStatus.OnHold);

        Assert.Equal(ContractStatus.OnHold, contract.Status);
        Assert.Equal(ServiceRequestStatus.OnHold, request.Status);
        Assert.Equal(AuditSeverity.Warning, Assert.Single(_audit.Entries).Severity);
        Assert.Equal(1, _contracts.Saves);
    }

    [Fact]
    public async Task ExpireOverdueContracts_ExpiresOnlyOverdue_CancelsTheirRequests()
    {
        var overdue = TestData.Contract(ContractStatus.Active, start: _clock.Today.AddYears(-1), end: _clock.Today.AddDays(-1), id: 1);
        var overdueRequest = TestData.Request(overdue);
        var current = TestData.Contract(ContractStatus.Active, id: 2);
        _contracts.Contracts.AddRange(new[] { overdue, current });

        var count = await _service.ExpireOverdueContractsAsync();

        Assert.Equal(1, count);
        Assert.Equal(ContractStatus.Expired, overdue.Status);
        Assert.Equal(ContractStatus.Active, current.Status);
        Assert.Equal(ServiceRequestStatus.Cancelled, overdueRequest.Status);
        Assert.Equal(AuditSeverity.Critical, Assert.Single(_audit.Entries).Severity);
    }

    [Fact]
    public async Task ExpireOverdueContracts_NothingOverdue_DoesNotSave()
    {
        _contracts.Contracts.Add(TestData.Contract(ContractStatus.Active, id: 1));

        Assert.Equal(0, await _service.ExpireOverdueContractsAsync());
        Assert.Equal(0, _contracts.Saves);
    }

    [Fact]
    public async Task Update_ExpiredContract_IsRejected()
    {
        _contracts.Contracts.Add(TestData.Contract(ContractStatus.Expired, id: 1));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.UpdateAsync(1, ValidInput()));
    }

    [Fact]
    public async Task Search_InvalidDateRange_IsRejected()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _service.SearchAsync(new GLMS.Core.Querying.ContractSearchCriteria { FromDate = _clock.Today, ToDate = _clock.Today.AddDays(-1) }));
    }

    /// <summary>Full workflow across services: create -> activate -> raise request -> hold -> blocked -> reactivate -> allowed.</summary>
    [Fact]
    public async Task FullWorkflow_RequestsBlockedWhileOnHold_AllowedAfterReactivation()
    {
        var requests = new FakeServiceRequestRepository();
        var validator = new ServiceRequestValidator(_clock);
        var requestService = new ServiceRequestService(requests, _contracts, validator,
            new FinancialService(new StubRateStrategy(18m)),
            new ServiceRequestFactoryResolver(new ServiceRequestFactory[] { new StandardSLAFactory(validator, _clock) }),
            _clock);

        var contract = await _service.CreateAsync(ValidInput(), Pdf(), activate: true);
        var first = await requestService.CreateAsync(new NewServiceRequest(contract.Id, "Work", 10m));
        contract.ServiceRequests.Add(first);
        Assert.Equal(180m, first.CostZar);

        await _service.ChangeStatusAsync(contract.Id, ContractStatus.OnHold);
        await Assert.ThrowsAsync<BusinessRuleException>(() => requestService.CreateAsync(new NewServiceRequest(contract.Id, "More", 5m)));
        Assert.Equal(ServiceRequestStatus.OnHold, first.Status);

        await _service.ChangeStatusAsync(contract.Id, ContractStatus.Active);
        var second = await requestService.CreateAsync(new NewServiceRequest(contract.Id, "More", 5m));

        Assert.Equal(ServiceRequestStatus.Pending, first.Status);
        Assert.Equal(2, requests.Requests.Count);
        Assert.Equal(90m, second.CostZar);
        Assert.Equal(3, _audit.Entries.Count); // activate, hold, re-activate
    }
}
