using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Factory;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;

namespace GLMS.Core.Services;

public sealed class ServiceRequestService
{
    private readonly IServiceRequestRepository _requests;
    private readonly IContractRepository _contracts;
    private readonly ServiceRequestValidator _validator;
    private readonly FinancialService _financial;
    private readonly ServiceRequestFactoryResolver _factories;
    private readonly IClock _clock;

    public ServiceRequestService(
        IServiceRequestRepository requests,
        IContractRepository contracts,
        ServiceRequestValidator validator,
        FinancialService financial,
        ServiceRequestFactoryResolver factories,
        IClock clock)
    {
        _requests = requests;
        _contracts = contracts;
        _validator = validator;
        _financial = financial;
        _factories = factories;
        _clock = clock;
    }

    public Task<IReadOnlyList<ServiceRequest>> GetAllAsync(ServiceRequestStatus? status = null, int? contractId = null, CancellationToken ct = default) =>
        _requests.GetAllAsync(status, contractId, ct);

    public async Task<ServiceRequest> GetAsync(int id, CancellationToken ct = default) =>
        await _requests.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Service request {id} was not found.");

    public Task<int> CountOpenAsync(CancellationToken ct = default) => _requests.CountOpenAsync(ct);

    /// <summary>Current USD->ZAR quote for the live preview on the create page.</summary>
    public Task<ExchangeRateQuote> GetUsdToZarQuoteAsync(CancellationToken ct = default) =>
        _financial.GetRateAsync(FinancialService.DefaultSourceCurrency, FinancialService.ReportingCurrency, ct);

    public async Task<ServiceRequest> CreateAsync(NewServiceRequest input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var contract = await _contracts.GetByIdAsync(input.ContractId, includeRequests: false, ct);

        // 1. Fail early - before spending an external API call.
        _validator.EnsureValid(contract, input);

        // 2. Strategy: convert the USD amount the user typed into ZAR (server-side, never trusting the browser).
        var conversion = await _financial.ConvertUsdToZarAsync(input.CostUsd, ct);

        // 3. Factory Method: the contract's SLA tier picks the factory, which re-validates and applies tier rules.
        var factory = _factories.Resolve(contract!.ServiceLevel);
        var request = factory.CreateRequest(contract, input, conversion);

        // Normalisation: reuse the stored row for this published rate instead of inserting a duplicate.
        request.ExchangeRate = await _requests.GetOrAddExchangeRateAsync(request.ExchangeRate!, ct);

        await _requests.AddAsync(request, ct);
        await _requests.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ServiceRequest> StartAsync(int id, CancellationToken ct = default)
    {
        var request = await GetAsync(id, ct);
        var contract = request.Contract ?? await _contracts.GetByIdAsync(request.ContractId, false, ct);
        if (contract is null || !_validator.CanRaiseRequests(contract))
            throw new BusinessRuleException("Work cannot start because the parent contract is no longer active.");

        request.Start();
        await _requests.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ServiceRequest> CompleteAsync(int id, CancellationToken ct = default)
    {
        var request = await GetAsync(id, ct);
        request.Complete(_clock.UtcNow);
        await _requests.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ServiceRequest> CancelAsync(int id, string? reason, CancellationToken ct = default)
    {
        var request = await GetAsync(id, ct);
        request.Cancel(string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason.Trim(), _clock.UtcNow);
        await _requests.SaveChangesAsync(ct);
        return request;
    }
}
