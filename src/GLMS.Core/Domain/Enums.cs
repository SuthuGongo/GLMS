namespace GLMS.Core.Domain;

/// <summary>Lifecycle states of a contract (Key Feature 1: automated status tracking).</summary>
public enum ContractStatus
{
    Draft = 0,
    Active = 1,
    OnHold = 2,
    Expired = 3
}

/// <summary>
/// Negotiated SLA tier. Each tier has its own concrete <c>ServiceRequestFactory</c>
/// (Factory Method pattern, Part 1 section 2.3).
/// </summary>
public enum ServiceLevel
{
    Standard = 0,
    PriorityLogistics = 1
}

public enum ServiceRequestStatus
{
    Pending = 0,
    InProgress = 1,
    OnHold = 2,
    Completed = 3,
    Cancelled = 4
}

public enum RequestPriority
{
    Normal = 0,
    High = 1,
    Critical = 2
}

public enum AuditSeverity
{
    Information = 0,
    Warning = 1,
    Critical = 2
}
