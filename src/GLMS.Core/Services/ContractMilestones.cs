using GLMS.Core.Domain;

namespace GLMS.Core.Services;

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

public sealed record Milestone(string Title, string Detail, MilestoneState State, DateTime? Date, int? RequestId = null);

/// <summary>
/// Turns a contract, its compliance audit trail and its service requests into the lifecycle timeline
/// on the contract page: Drafted, Signed, Activated, holds/re-activations, open work, then expiry.
/// </summary>
public static class ContractMilestones
{
    public static IReadOnlyList<Milestone> Build(Contract contract, IEnumerable<ContractAuditEntry> auditTrail, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var audit = (auditTrail ?? Enumerable.Empty<ContractAuditEntry>()).OrderBy(a => a.OccurredUtc).ToList();
        var client = contract.Client?.Name ?? "the client";

        var reached = new List<Milestone>
        {
            new("Drafted", $"Contract created for {client}", MilestoneState.Reached, contract.CreatedUtc)
        };
        var todo = new List<Milestone>();
        var upcoming = new List<Milestone>();

        // Signed agreement
        if (contract.HasSignedAgreement)
            reached.Add(new("Signed agreement on file", contract.SignedAgreementOriginalName ?? "Signed agreement (PDF)", MilestoneState.Reached, contract.SignedAgreementUploadedUtc));
        else if (contract.Status != ContractStatus.Expired)
            todo.Add(new("Upload the signed agreement", "The contract can't be activated until the signed PDF is on file", MilestoneState.ToDo, null));

        // Activation and later status changes, taken from the ComplianceLogger audit trail
        var firstActivation = audit.FirstOrDefault(a => a.ToStatus == ContractStatus.Active);
        if (firstActivation is not null || contract.Status != ContractStatus.Draft)
        {
            reached.Add(new("Activated", "Opened for service requests", MilestoneState.Reached, firstActivation?.OccurredUtc));
        }
        else if (contract.HasSignedAgreement)
        {
            todo.Add(new("Activate the contract", "The signed agreement is on file, so it can be activated", MilestoneState.ToDo, null));
        }
        else
        {
            upcoming.Add(new("Activate", "Possible once the signed agreement is uploaded", MilestoneState.Upcoming, null));
        }

        foreach (var entry in audit.Where(a => a != firstActivation))
        {
            switch (entry.ToStatus)
            {
                case ContractStatus.OnHold:
                    reached.Add(new("Put on hold", "New service requests were blocked and open ones paused", MilestoneState.Reached, entry.OccurredUtc));
                    break;
                case ContractStatus.Active:
                    reached.Add(new("Re-activated", "Service requests resumed", MilestoneState.Reached, entry.OccurredUtc));
                    break;
            }
        }

        // Open work on this contract
        foreach (var request in contract.ServiceRequests.Where(r => r.IsOverdue(utcNow)).OrderBy(r => r.ResponseDueUtc))
            todo.Add(new($"Respond to request #{request.Id}", $"{Shorten(request.Description)}. Past its response time.", MilestoneState.ToDo, request.ResponseDueUtc, request.Id));

        if (contract.Status == ContractStatus.OnHold)
            todo.Add(new("Re-activate or expire the contract", "The client can't raise service requests while it is on hold", MilestoneState.ToDo, null));

        // Expiry
        if (contract.Status == ContractStatus.Expired)
        {
            var expiredAt = audit.LastOrDefault(a => a.ToStatus == ContractStatus.Expired)?.OccurredUtc ?? contract.EndDate;
            reached.Add(new("Expired", "Closed record. Service requests are blocked", MilestoneState.Reached, expiredAt));
        }
        else
        {
            upcoming.Add(new("Expires", "Moves to Expired automatically and blocks new service requests", MilestoneState.Upcoming, contract.EndDate));
        }

        // Reached milestones in date order (undated ones keep their logical position), then to-dos, then upcoming.
        // An undated milestone (e.g. seeded data with no audit entry) sorts right after the one before it.
        var keyed = new List<(Milestone m, DateTime key, int index)>();
        var lastKey = DateTime.MinValue;
        for (var i = 0; i < reached.Count; i++)
        {
            lastKey = reached[i].Date ?? lastKey;
            keyed.Add((reached[i], lastKey, i));
        }
        var orderedReached = keyed.OrderBy(x => x.key).ThenBy(x => x.index).Select(x => x.m);

        return orderedReached.Concat(todo).Concat(upcoming).ToList();
    }

    private static string Shorten(string text, int max = 60)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }
}
