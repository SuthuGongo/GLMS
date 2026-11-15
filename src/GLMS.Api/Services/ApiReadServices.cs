using GLMS.Api.Mapping;
using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Querying;
using GLMS.Core.Services;
using GLMS.Shared;
using GLMS.Shared.Dtos;

namespace GLMS.Api.Services;

/// <summary>
/// Builds the read models the front end needs (contract details page, home dashboard) from the GLMS.Core
/// services. Keeping this here means the API controllers stay thin and the MVC app gets everything it shows
/// in ONE call, already worked out by the server's business rules.
/// </summary>
public sealed class ContractReadService
{
    private readonly ContractService _contracts;
    private readonly ServiceRequestValidator _validator;
    private readonly IClock _clock;

    public ContractReadService(ContractService contracts, ServiceRequestValidator validator, IClock clock)
    {
        _contracts = contracts;
        _validator = validator;
        _clock = clock;
    }

    public async Task<ContractDetailsDto> GetDetailsAsync(int id, CancellationToken ct = default)
    {
        var contract = await _contracts.GetAsync(id, ct);           // throws NotFoundException -> 404
        var audit = await _contracts.GetAuditTrailAsync(id, ct);
        var blockers = _validator.ValidateContract(contract);       // Observer/validator rules from Part 2

        var dto = DtoMapper.Fill(new ContractDetailsDto(), contract);
        dto.ServiceRequests = contract.ServiceRequests
            .OrderByDescending(r => r.CreatedUtc)
            .Select(r => r.ToDto(contract))
            .ToList();
        dto.AuditTrail = audit.Select(a => a.ToDto()).ToList();
        dto.Milestones = ContractMilestones.Build(contract, audit, _clock.UtcNow).Select(m => m.ToDto()).ToList();
        dto.NextStatuses = Contract.NextStatuses(contract.Status).ToList();
        dto.CanRaiseRequests = blockers.Count == 0;
        dto.RequestBlockers = blockers.ToList();
        dto.Today = DtoMapper.DateOnlyValue(_clock.Today);
        return dto;
    }
}

public sealed class DashboardService
{
    private const int ContractsShownOnHome = 6;

    private readonly ContractService _contracts;
    private readonly ServiceRequestService _requests;
    private readonly IClock _clock;

    public DashboardService(ContractService contracts, ServiceRequestService requests, IClock clock)
    {
        _contracts = contracts;
        _requests = requests;
        _clock = clock;
    }

    /// <summary>The action-first home page: "To do" list (WorkQueue), contract term lines, request counts and the live rate.</summary>
    public async Task<DashboardDto> BuildAsync(CancellationToken ct = default)
    {
        var today = _clock.Today;
        var now = _clock.UtcNow;

        var contracts = await _contracts.SearchAsync(new ContractSearchCriteria(), ct);
        var requests = await _requests.GetAllAsync(ct: ct);

        var dto = new DashboardDto
        {
            Today = DtoMapper.DateOnlyValue(today),
            UtcNow = DateTime.SpecifyKind(now, DateTimeKind.Utc),
            WorkItems = WorkQueue.Build(contracts, requests, now, today).Select(w => w.ToDto()).ToList(),
            Requests = RequestSummary.Build(requests, now).ToDto(),
            TotalContracts = contracts.Count,
            ContractsOpenForWork = contracts.Count(c => c.Status == ContractStatus.Active && c.IsWithinTerm(today)),
            Contracts = contracts
                .Where(c => c.Status != ContractStatus.Expired || c.EndDate >= today.AddDays(-30)) // hide old closed records
                .OrderBy(c => c.Status switch
                {
                    ContractStatus.Active => 0,
                    ContractStatus.OnHold => 1,
                    ContractStatus.Draft => 2,
                    _ => 3
                })
                .ThenBy(c => c.EndDate)
                .Take(ContractsShownOnHome)
                .Select(c => c.ToDto())
                .ToList()
        };

        try
        {
            dto.UsdToZar = (await _requests.GetUsdToZarQuoteAsync(ct)).ToDto();
        }
        catch (CurrencyConversionException ex)
        {
            dto.RateError = ex.Message; // the dashboard still loads when the currency API is down
        }

        return dto;
    }
}

public static class QueryMapping
{
    public static ContractSearchCriteria ToCriteria(this ContractSearchQuery q) => new()
    {
        FromDate = q.FromDate,
        ToDate = q.ToDate,
        Status = q.Status,
        ServiceLevel = q.ServiceLevel,
        ClientId = q.ClientId,
        Keyword = q.Keyword
    };

    public static ContractInput ToInput(this SaveContractRequest r) =>
        new(r.ClientId, r.Title, r.StartDate, r.EndDate, r.ServiceLevel);

    public static ClientInput ToInput(this SaveClientRequest r) =>
        new(r.Name, r.ContactPerson ?? string.Empty, r.Email, r.PhoneNumber ?? string.Empty, r.Region);
}
