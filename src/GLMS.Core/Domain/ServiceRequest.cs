using GLMS.Core.Exceptions;

namespace GLMS.Core.Domain;

/// <summary>
/// Work raised by a Logistics Manager against a contract.
/// New instances are created ONLY through a <c>ServiceRequestFactory</c> (Factory Method),
/// which guarantees validation and SLA rules have been applied.
/// </summary>
public class ServiceRequest
{
    public int Id { get; set; }

    public int ContractId { get; set; }
    public Contract? Contract { get; set; }

    public string Description { get; set; } = string.Empty;

    // ---- Financial integration: what the user typed (USD) and what we report in (ZAR).
    // The rate itself (value, source, publish time) is a separate fact, stored once in ExchangeRates
    // and referenced here, so rate details are never repeated on every request (3NF).
    public decimal CostUsd { get; set; }
    public decimal CostZar { get; set; }

    public int ExchangeRateId { get; set; }
    public ExchangeRate? ExchangeRate { get; set; }

    // Convenience read-only views of the referenced rate (not columns).
    public decimal ExchangeRateUsed => ExchangeRate?.Rate ?? 0m;
    public string RateSource => ExchangeRate?.Source ?? string.Empty;
    public DateTime RateRetrievedUtc => ExchangeRate?.PublishedUtc ?? default;

    public ServiceRequestStatus Status { get; private set; } = ServiceRequestStatus.Pending;
    public RequestPriority Priority { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime ResponseDueUtc { get; set; }
    public DateTime? ClosedUtc { get; private set; }

    /// <summary>Why the system (not a user) last changed the status, e.g. "Contract expired".</summary>
    public string? StatusReason { get; private set; }

    public bool IsOpen => Status is ServiceRequestStatus.Pending or ServiceRequestStatus.InProgress or ServiceRequestStatus.OnHold;

    /// <summary>
    /// Open, not paused, and past its SLA response time. Requests on hold are not counted as
    /// overdue because the clock is stopped while their contract is on hold.
    /// </summary>
    public bool IsOverdue(DateTime utcNow) =>
        Status is ServiceRequestStatus.Pending or ServiceRequestStatus.InProgress && ResponseDueUtc < utcNow;

    /// <summary>Open, not paused, not yet overdue, and due within <paramref name="window"/>.</summary>
    public bool IsDueWithin(DateTime utcNow, TimeSpan window) =>
        Status is ServiceRequestStatus.Pending or ServiceRequestStatus.InProgress &&
        ResponseDueUtc >= utcNow && ResponseDueUtc <= utcNow + window;

    // ---- User-driven transitions

    public void Start()
    {
        EnsureStatus(ServiceRequestStatus.Pending, "start");
        Status = ServiceRequestStatus.InProgress;
        StatusReason = null;
    }

    public void Complete(DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.InProgress, "complete");
        Status = ServiceRequestStatus.Completed;
        ClosedUtc = utcNow;
        StatusReason = null;
    }

    public void Cancel(string reason, DateTime utcNow)
    {
        if (!IsOpen)
            throw new InvalidStatusTransitionException($"Service request #{Id} is already {Status} and cannot be cancelled.");

        Status = ServiceRequestStatus.Cancelled;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by user" : reason;
        ClosedUtc = utcNow;
    }

    // ---- System-driven transitions (called by the ServiceRequestValidator observer)

    public void PutOnHold(string reason)
    {
        if (Status is ServiceRequestStatus.Pending or ServiceRequestStatus.InProgress)
        {
            Status = ServiceRequestStatus.OnHold;
            StatusReason = reason;
        }
    }

    public void Resume(string reason)
    {
        if (Status == ServiceRequestStatus.OnHold)
        {
            Status = ServiceRequestStatus.Pending;
            StatusReason = reason;
        }
    }

    private void EnsureStatus(ServiceRequestStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidStatusTransitionException(
                $"Cannot {action} service request #{Id} because it is {Status} (expected {expected}).");
    }

    /// <summary>For seeding/import only.</summary>
    public static void ImportStatus(ServiceRequest request, ServiceRequestStatus status) => request.Status = status;
}
