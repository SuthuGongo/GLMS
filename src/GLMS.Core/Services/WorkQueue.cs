using GLMS.Core.Domain;

namespace GLMS.Core.Services;

/// <summary>What kind of follow-up a work item asks for.</summary>
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

/// <summary>One line on the home page "To do" list.</summary>
public sealed record WorkItem(
    WorkItemKind Kind,
    WorkItemUrgency Urgency,
    Contract Contract,
    ServiceRequest? Request,
    DateTime SortKey);

/// <summary>
/// Builds the home page "To do" list: the things a Logistics Manager has to act on, most urgent first.
/// Inspired by action-first freight platforms (e.g. Flexport's to-do checklist): the system tells the
/// user what needs attention instead of making them hunt through tables.
/// </summary>
public static class WorkQueue
{
    public const int EndingSoonDays = 30;
    public static readonly TimeSpan DueSoonWindow = TimeSpan.FromHours(24);

    public static IReadOnlyList<WorkItem> Build(IEnumerable<Contract> contracts, IEnumerable<ServiceRequest> requests, DateTime utcNow, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(requests);

        var contractList = contracts.ToList();
        var byId = contractList.ToDictionary(c => c.Id);
        var items = new List<WorkItem>();

        foreach (var request in requests)
        {
            var contract = request.Contract ?? (byId.TryGetValue(request.ContractId, out var c) ? c : null);
            if (contract is null) continue;

            if (request.IsOverdue(utcNow))
                items.Add(new WorkItem(WorkItemKind.OverdueRequest, WorkItemUrgency.Critical, contract, request, request.ResponseDueUtc));
            else if (request.IsDueWithin(utcNow, DueSoonWindow))
                items.Add(new WorkItem(WorkItemKind.RequestDueSoon, WorkItemUrgency.Warning, contract, request, request.ResponseDueUtc));
        }

        foreach (var contract in contractList)
        {
            switch (contract.Status)
            {
                case ContractStatus.Draft:
                    items.Add(contract.HasSignedAgreement
                        ? new WorkItem(WorkItemKind.ReadyToActivate, WorkItemUrgency.Action, contract, null, contract.StartDate)
                        : new WorkItem(WorkItemKind.AwaitingSignature, WorkItemUrgency.Action, contract, null, contract.StartDate));
                    break;

                case ContractStatus.OnHold when contract.IsOverdueForExpiry(today):
                case ContractStatus.Active when contract.IsOverdueForExpiry(today):
                    items.Add(new WorkItem(WorkItemKind.AwaitingExpiry, WorkItemUrgency.Warning, contract, null, contract.EndDate));
                    break;

                case ContractStatus.OnHold:
                    items.Add(new WorkItem(WorkItemKind.ContractOnHold, WorkItemUrgency.Warning, contract, null, contract.EndDate));
                    break;

                case ContractStatus.Active when contract.DaysRemaining(today) <= EndingSoonDays:
                    items.Add(new WorkItem(WorkItemKind.ContractEndingSoon, WorkItemUrgency.Warning, contract, null, contract.EndDate));
                    break;
            }
        }

        return items
            .OrderBy(i => i.Urgency)
            .ThenBy(i => i.SortKey)
            .ToList();
    }
}

/// <summary>Counts for the home page "Open requests" panel.</summary>
public sealed record RequestSummary(int Overdue, int DueSoon, int OnTrack, int Paused, decimal ValueThisMonthZar)
{
    public static RequestSummary Build(IEnumerable<ServiceRequest> requests, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var list = requests.ToList();
        var open = list.Where(r => r.IsOpen).ToList();

        var overdue = open.Count(r => r.IsOverdue(utcNow));
        var dueSoon = open.Count(r => r.IsDueWithin(utcNow, WorkQueue.DueSoonWindow));
        var paused = open.Count(r => r.Status == ServiceRequestStatus.OnHold);
        var onTrack = open.Count - overdue - dueSoon - paused;

        // Value of work raised this calendar month (UTC), excluding cancelled requests.
        var value = list
            .Where(r => r.Status != ServiceRequestStatus.Cancelled &&
                        r.CreatedUtc.Year == utcNow.Year && r.CreatedUtc.Month == utcNow.Month)
            .Sum(r => r.CostZar);

        return new RequestSummary(overdue, dueSoon, onTrack, paused, value);
    }
}
