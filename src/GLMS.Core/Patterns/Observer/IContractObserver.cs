using GLMS.Core.Domain;

namespace GLMS.Core.Patterns.Observer;

/// <summary>
/// OBSERVER PATTERN (Part 1, section 2.2) - the Observer interface.
/// <see cref="Contract.ChangeStatus"/> calls this on every subscriber after a status change.
/// </summary>
public interface IContractObserver
{
    void OnStatusChanged(Contract contract, ContractStatus previousStatus);
}
