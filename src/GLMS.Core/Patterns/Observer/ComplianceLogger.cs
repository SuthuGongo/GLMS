using GLMS.Core.Abstractions;
using GLMS.Core.Domain;

namespace GLMS.Core.Patterns.Observer;
public sealed class ComplianceLogger : IContractObserver
{
    private readonly IAuditSink _sink;
    private readonly IClock _clock;

    public ComplianceLogger(IAuditSink sink, IClock clock)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void OnStatusChanged(Contract contract, ContractStatus previousStatus)
    {
        var severity = contract.Status switch
        {
            ContractStatus.Expired => AuditSeverity.Critical,
            ContractStatus.OnHold => AuditSeverity.Warning,
            _ => AuditSeverity.Information
        };

        var message = $"Contract {contract.Display} changed from {previousStatus} to {contract.Status}.";
        if (contract.Status is ContractStatus.Expired or ContractStatus.OnHold)
            message += " New service requests are now blocked.";

        _sink.Record(new ContractAuditEntry
        {
            ContractId = contract.Id,
            FromStatus = previousStatus,
            ToStatus = contract.Status,
            Severity = severity,
            Message = message,
            OccurredUtc = _clock.UtcNow
        });
    }
}
