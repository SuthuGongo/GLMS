using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Querying;
using GLMS.Core.Validation;

namespace GLMS.Core.Services;

public sealed record ContractInput(int ClientId, string Title, DateTime StartDate, DateTime EndDate, ServiceLevel ServiceLevel);

/// <summary>An uploaded file, independent of ASP.NET's IFormFile so the service stays testable.</summary>
public sealed record FileUpload(Stream Content, string FileName, long Length);

public sealed record AgreementDownload(Stream Content, string DownloadName);

/// <summary>
/// Contract Management Hub use cases. Wires the injected observers onto each contract before
/// any status change, so ComplianceLogger and ServiceRequestValidator always react.
/// </summary>
public sealed class ContractService
{
    private readonly IContractRepository _contracts;
    private readonly IClientRepository _clients;
    private readonly IFileStorageService _files;
    private readonly IEnumerable<IContractObserver> _observers;
    private readonly IClock _clock;

    public ContractService(
        IContractRepository contracts,
        IClientRepository clients,
        IFileStorageService files,
        IEnumerable<IContractObserver> observers,
        IClock clock)
    {
        _contracts = contracts;
        _clients = clients;
        _files = files;
        _observers = observers;
        _clock = clock;
    }

    public Task<IReadOnlyList<Contract>> SearchAsync(ContractSearchCriteria criteria, CancellationToken ct = default)
    {
        var errors = criteria.Validate();
        if (errors.Count > 0) throw new BusinessRuleException(errors);
        return _contracts.SearchAsync(criteria, ct);
    }

    public async Task<Contract> GetAsync(int id, CancellationToken ct = default) =>
        await _contracts.GetByIdAsync(id, includeRequests: true, ct) ?? throw new NotFoundException($"Contract {id} was not found.");

    public Task<IReadOnlyList<ContractAuditEntry>> GetAuditTrailAsync(int id, CancellationToken ct = default) =>
        _contracts.GetAuditTrailAsync(id, ct);

    public Task<IReadOnlyList<Contract>> GetOpenForRequestsAsync(CancellationToken ct = default) =>
        _contracts.GetOpenForRequestsAsync(_clock.Today, ct);

    public Task<IReadOnlyDictionary<ContractStatus, int>> CountByStatusAsync(CancellationToken ct = default) =>
        _contracts.CountByStatusAsync(ct);

    /// <summary>Creates a contract (always starts as Draft). Optionally attaches the PDF and activates it.</summary>
    public async Task<Contract> CreateAsync(ContractInput input, FileUpload? signedAgreement, bool activate, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);

        if (activate)
        {
            var activationErrors = ContractValidator.ValidateActivation(input, signedAgreement is not null, _clock.Today);
            if (activationErrors.Count > 0) throw new BusinessRuleException(activationErrors);
        }

        var contract = new Contract
        {
            ContractNumber = GenerateContractNumber(),
            ClientId = input.ClientId,
            Title = input.Title.Trim(),
            StartDate = input.StartDate.Date,
            EndDate = input.EndDate.Date,
            ServiceLevel = input.ServiceLevel,
            CreatedUtc = _clock.UtcNow
        };
        Subscribe(contract);

        StoredFile? stored = null;
        try
        {
            if (signedAgreement is not null)
            {
                stored = await _files.SaveAsync(signedAgreement.Content, signedAgreement.FileName, signedAgreement.Length, ct);
                contract.AttachSignedAgreement(stored.StoredFileName, stored.OriginalFileName, _clock.UtcNow, stored.SizeBytes);
            }

            await _contracts.AddAsync(contract, ct);
            await _contracts.SaveChangesAsync(ct); // need the Id before auditing activation

            if (activate)
            {
                contract.ChangeStatus(ContractStatus.Active); // observers log the activation
                await _contracts.SaveChangesAsync(ct);
            }
        }
        catch when (stored is not null && contract.Id == 0)
        {
            _files.Delete(stored.StoredFileName); // compensate: no orphan files
            throw;
        }

        return contract;
    }

    public async Task<Contract> UpdateAsync(int id, ContractInput input, byte[]? expectedVersion = null, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);
        var contract = await GetAsync(id, ct);
        if (expectedVersion is { Length: > 0 }) _contracts.SetExpectedVersion(contract, expectedVersion);

        if (contract.Status == ContractStatus.Expired)
            throw new BusinessRuleException("Expired contracts are closed legal records and cannot be edited.");

        if (contract.ServiceLevel != input.ServiceLevel && contract.ServiceRequests.Any(r => r.IsOpen))
            throw new BusinessRuleException("The SLA tier cannot be changed while the contract has open service requests.");

        contract.ClientId = input.ClientId;
        contract.Title = input.Title.Trim();
        contract.StartDate = input.StartDate.Date;
        contract.EndDate = input.EndDate.Date;
        contract.ServiceLevel = input.ServiceLevel;

        await _contracts.SaveChangesAsync(ct);
        return contract;
    }

    public async Task<Contract> UploadSignedAgreementAsync(int id, FileUpload upload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var contract = await GetAsync(id, ct);

        if (contract.Status == ContractStatus.Expired)
            throw new BusinessRuleException("The signed agreement of an expired contract cannot be replaced.");

        // Validation happens inside SaveAsync - an .exe never reaches the disk or the DB.
        var stored = await _files.SaveAsync(upload.Content, upload.FileName, upload.Length, ct);
        var previous = contract.SignedAgreementFileName;

        try
        {
            contract.AttachSignedAgreement(stored.StoredFileName, stored.OriginalFileName, _clock.UtcNow, stored.SizeBytes);
            await _contracts.SaveChangesAsync(ct);
        }
        catch
        {
            _files.Delete(stored.StoredFileName);
            throw;
        }

        if (!string.IsNullOrEmpty(previous)) _files.Delete(previous);
        return contract;
    }

    public async Task<AgreementDownload> DownloadSignedAgreementAsync(int id, CancellationToken ct = default)
    {
        var contract = await _contracts.GetByIdAsync(id, false, ct) ?? throw new NotFoundException($"Contract {id} was not found.");
        if (!contract.HasSignedAgreement)
            throw new NotFoundException($"Contract {contract.Display} has no signed agreement uploaded.");

        var stream = await _files.OpenReadAsync(contract.SignedAgreementFileName!, ct);
        var name = $"{contract.ContractNumber}_{contract.SignedAgreementOriginalName}";
        return new AgreementDownload(stream, name);
    }

    public async Task<Contract> ChangeStatusAsync(int id, ContractStatus newStatus, byte[]? expectedVersion = null, CancellationToken ct = default)
    {
        var statusErrors = ContractValidator.ValidateStatus(newStatus);
        if (statusErrors.Count > 0) throw new BusinessRuleException(statusErrors);

        var contract = await GetAsync(id, ct); // includes ServiceRequests for the validator observer
        if (expectedVersion is { Length: > 0 }) _contracts.SetExpectedVersion(contract, expectedVersion);

        if (newStatus == ContractStatus.Active && contract.EndDate.Date < _clock.Today)
            throw new BusinessRuleException(
                $"Contract {contract.Display} ended on {contract.EndDate:yyyy-MM-dd}; it cannot be re-activated. Extend the end date first or mark it Expired.");

        Subscribe(contract);
        contract.ChangeStatus(newStatus); // throws InvalidStatusTransitionException for illegal moves
        await _contracts.SaveChangesAsync(ct);
        return contract;
    }

    /// <summary>
    /// Automated status tracking: expires every Active/On Hold contract whose end date has passed.
    /// Runs from a background worker; observers cancel the open requests and write the audit trail.
    /// </summary>
    public async Task<int> ExpireOverdueContractsAsync(CancellationToken ct = default)
    {
        var overdue = await _contracts.GetOverdueForExpiryAsync(_clock.Today, ct);
        foreach (var contract in overdue)
        {
            Subscribe(contract);
            contract.ChangeStatus(ContractStatus.Expired);
        }

        if (overdue.Count > 0)
            await _contracts.SaveChangesAsync(ct);

        return overdue.Count;
    }

    private void Subscribe(Contract contract)
    {
        foreach (var observer in _observers)
            contract.Subscribe(observer);
    }

    private async Task ValidateAsync(ContractInput? input, CancellationToken ct)
    {
        // Field rules live in ContractValidator; only the database lookup happens here.
        var errors = ContractValidator.Validate(input).ToList();
        if (input is not null && input.ClientId > 0 && !await _clients.ExistsAsync(input.ClientId, ct))
            errors.Add("Choose an existing client.");
        if (errors.Count > 0) throw new BusinessRuleException(errors);
    }

    private string GenerateContractNumber() =>
        $"TM-{_clock.Today:yyyy}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}
