namespace GLMS.Shared;

// Part 3: these enums moved here from GLMS.Core.Domain so the domain, the Web API and the MVC client all use
// ONE definition. They travel over the wire as text ("Active", "PriorityLogistics"), see GlmsJson.

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

/// <summary>What kind of follow-up a home page "To do" item asks for.</summary>
public enum WorkItemKind
{
    OverdueRequest,      // an open request is past its SLA response time
    RequestDueSoon,      // an open request is due within the next 24 hours
    AwaitingSignature,   // a draft contract has no signed PDF yet
    ReadyToActivate,     // a draft contract has its PDF and can be activated
    ContractOnHold,      // a contract is on hold, so its client can't raise requests
    ContractEndingSoon,  // an active contract ends within 30 days
    AwaitingExpiry       // an active/on-hold contract is past its end date and will be expired by the background job
}

public enum WorkItemUrgency
{
    Critical = 0,
    Warning = 1,
    Action = 2
}

/// <summary>
/// Reached = done, ToDo = needs someone to act now, Upcoming = will happen later.
/// (Same three states freight platforms such as Shypple use on their shipment timelines.)
/// </summary>
public enum MilestoneState
{
    Reached,
    ToDo,
    Upcoming
}

/// <summary>Roles carried in the JWT. Admins may also delete clients.</summary>
public static class GlmsRoles
{
    public const string Admin = "Admin";
    public const string Manager = "LogisticsManager";
}
