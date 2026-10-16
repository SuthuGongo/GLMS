namespace GLMS.Core.Domain;

/// <summary>Compliance audit trail written by the <c>ComplianceLogger</c> observer.</summary>
public class ContractAuditEntry
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public ContractStatus FromStatus { get; set; }
    public ContractStatus ToStatus { get; set; }
    public AuditSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
}
