using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;

namespace GLMS.Core.Patterns.Factory;

/// <summary>
/// FACTORY METHOD PATTERN (Part 1, section 2.3) - the abstract Creator.
///
/// <see cref="CreateRequest"/> is a fixed algorithm (validate -> apply tier rules -> build):
/// validation can never be skipped by a caller, and each SLA tier only overrides the steps
/// that differ (response window, priority, tier-specific rules). Adding a new SLA tier means
/// adding one subclass and one DI registration - nothing else changes.
/// </summary>
public abstract class ServiceRequestFactory
{
    private readonly ServiceRequestValidator _validator;
    protected readonly IClock Clock;

    protected ServiceRequestFactory(ServiceRequestValidator validator, IClock clock)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>The SLA tier this factory builds requests for.</summary>
    public abstract ServiceLevel ServiceLevel { get; }

    /// <summary>The factory method: creates a fully validated, SLA-correct ServiceRequest.</summary>
    public ServiceRequest CreateRequest(Contract contract, NewServiceRequest input, CurrencyConversion conversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);

        _validator.EnsureValid(contract, input);

        if (contract.ServiceLevel != ServiceLevel)
            throw new InvalidOperationException(
                $"{GetType().Name} builds {ServiceLevel} requests but contract {contract.Display} is {contract.ServiceLevel}.");

        if (conversion.SourceAmount != input.CostUsd)
            throw new InvalidOperationException("The currency conversion does not match the requested cost.");

        var tierErrors = ValidateTierRules(contract, input).ToList();
        if (tierErrors.Count > 0)
            throw new BusinessRuleException(tierErrors);

        var now = Clock.UtcNow;
        var request = new ServiceRequest
        {
            ContractId = contract.Id,
            Description = input.Description.Trim(),
            CostUsd = conversion.SourceAmount,
            CostZar = conversion.ConvertedAmount,
            ExchangeRate = new ExchangeRate
            {
                BaseCurrency = conversion.FromCurrency,
                QuoteCurrency = conversion.ToCurrency,
                Rate = conversion.Rate,
                Source = conversion.RateSource,
                PublishedUtc = ExchangeRate.ToStoredPrecision(conversion.RateRetrievedUtc)
            },
            CreatedUtc = now,
            Priority = DeterminePriority(input),
        };
        request.ResponseDueUtc = now + GetResponseWindow(request.Priority);
        return request;
    }

    /// <summary>Hook: rules that only apply to this tier. Default: none.</summary>
    protected virtual IEnumerable<string> ValidateTierRules(Contract contract, NewServiceRequest input) =>
        Enumerable.Empty<string>();

    protected abstract RequestPriority DeterminePriority(NewServiceRequest input);

    protected abstract TimeSpan GetResponseWindow(RequestPriority priority);
}

/// <summary>CONCRETE CREATOR - Standard SLA: normal priority, 48-hour response, no urgent escalation.</summary>
public sealed class StandardSLAFactory : ServiceRequestFactory
{
    public static readonly TimeSpan ResponseWindow = TimeSpan.FromHours(48);

    public StandardSLAFactory(ServiceRequestValidator validator, IClock clock) : base(validator, clock) { }

    public override ServiceLevel ServiceLevel => ServiceLevel.Standard;

    protected override IEnumerable<string> ValidateTierRules(Contract contract, NewServiceRequest input)
    {
        if (input.IsUrgent)
            yield return "Urgent escalation is only available on Priority Logistics contracts. Untick 'urgent' or upgrade the contract's SLA.";
    }

    protected override RequestPriority DeterminePriority(NewServiceRequest input) => RequestPriority.Normal;

    protected override TimeSpan GetResponseWindow(RequestPriority priority) => ResponseWindow;
}

/// <summary>CONCRETE CREATOR - Priority Logistics SLA: high priority (8h), urgent escalates to critical (2h).</summary>
public sealed class PriorityLogisticsFactory : ServiceRequestFactory
{
    public static readonly TimeSpan HighWindow = TimeSpan.FromHours(8);
    public static readonly TimeSpan CriticalWindow = TimeSpan.FromHours(2);

    public PriorityLogisticsFactory(ServiceRequestValidator validator, IClock clock) : base(validator, clock) { }

    public override ServiceLevel ServiceLevel => ServiceLevel.PriorityLogistics;

    protected override RequestPriority DeterminePriority(NewServiceRequest input) =>
        input.IsUrgent ? RequestPriority.Critical : RequestPriority.High;

    protected override TimeSpan GetResponseWindow(RequestPriority priority) =>
        priority == RequestPriority.Critical ? CriticalWindow : HighWindow;
}
public sealed class ServiceRequestFactoryResolver
{
    private readonly IReadOnlyDictionary<ServiceLevel, ServiceRequestFactory> _factories;

    public ServiceRequestFactoryResolver(IEnumerable<ServiceRequestFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        var list = factories.ToList();

        var duplicate = list.GroupBy(f => f.ServiceLevel).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"More than one factory is registered for {duplicate.Key}.");

        _factories = list.ToDictionary(f => f.ServiceLevel);
    }
    public ServiceRequestFactory Resolve(ServiceLevel serviceLevel) =>
        _factories.TryGetValue(serviceLevel, out var factory)
            ? factory
            : throw new InvalidOperationException($"No service request factory is registered for SLA tier {serviceLevel}.");
}
