using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;

namespace GLMS.Core.Patterns.Observer;


public sealed record NewServiceRequest(int ContractId, string Description, decimal CostUsd, bool IsUrgent = false);

public sealed class ServiceRequestValidator : IContractObserver
{
    public const int MaxDescriptionLength = 1000;
    public const decimal MaxCostUsd = 10_000_000m;

    private readonly IClock _clock;

    public ServiceRequestValidator(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public IReadOnlyList<string> Validate(Contract? contract, NewServiceRequest? input)
    {
        var errors = new List<string>();

        if (contract is null)
        {
            errors.Add("The selected contract does not exist.");
        }
        else
        {
            errors.AddRange(ValidateContract(contract));
        }

        if (input is null)
        {
            errors.Add("Service request details are required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(input.Description))
            errors.Add("A description of the service required is mandatory.");
        else if (input.Description.Trim().Length > MaxDescriptionLength)
            errors.Add($"The description cannot be longer than {MaxDescriptionLength} characters.");

        if (input.CostUsd <= 0)
            errors.Add("The cost (USD) must be greater than zero.");
        else if (input.CostUsd > MaxCostUsd)
            errors.Add($"The cost (USD) cannot exceed {MaxCostUsd:N0}.");
        else if (decimal.Round(input.CostUsd, 2) != input.CostUsd)
            errors.Add("The cost (USD) cannot have more than 2 decimal places.");

        return errors;
    }

    /// <summary>Contract-only checks - used to filter the contract dropdown and before calling the currency API.</summary>
    public IReadOnlyList<string> ValidateContract(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var errors = new List<string>();
        var today = _clock.Today;

        switch (contract.Status)
        {
            case ContractStatus.Expired:
                errors.Add($"Contract {contract.Display} has EXPIRED - service requests cannot be raised against it.");
                break;
            case ContractStatus.OnHold:
                errors.Add($"Contract {contract.Display} is ON HOLD - service requests cannot be raised until it is re-activated.");
                break;
            case ContractStatus.Draft:
                errors.Add($"Contract {contract.Display} is still a DRAFT - it must be signed and activated first.");
                break;
        }

        if (contract.Status == ContractStatus.Active)
        {
            if (today < contract.StartDate.Date)
                errors.Add($"Contract {contract.Display} only starts on {contract.StartDate:yyyy-MM-dd}.");
            if (today > contract.EndDate.Date)
                errors.Add($"Contract {contract.Display} ended on {contract.EndDate:yyyy-MM-dd} and is awaiting expiry.");
        }

        return errors;
    }

    public bool CanRaiseRequests(Contract contract) => ValidateContract(contract).Count == 0;

    /// <summary>Throws <see cref="BusinessRuleException"/> listing every broken rule.</summary>
    public void EnsureValid(Contract? contract, NewServiceRequest? input)
    {
        var errors = Validate(contract, input);
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);
    }

    //Observer callback

    public void OnStatusChanged(Contract contract, ContractStatus previousStatus)
    {
        var open = contract.ServiceRequests.Where(r => r.IsOpen).ToList();
        var now = _clock.UtcNow;

        switch (contract.Status)
        {
            case ContractStatus.OnHold:
                foreach (var request in open)
                    request.PutOnHold($"Contract {contract.Display} was put on hold.");
                break;

            case ContractStatus.Expired:
                foreach (var request in open)
                    request.Cancel($"Contract {contract.Display} expired.", now);
                break;

            case ContractStatus.Active when previousStatus == ContractStatus.OnHold:
                foreach (var request in open)
                    request.Resume($"Contract {contract.Display} was re-activated.");
                break;
        }
    }
}
