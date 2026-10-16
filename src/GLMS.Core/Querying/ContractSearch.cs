using GLMS.Core.Domain;

namespace GLMS.Core.Querying;

/// <summary>Admin search/filter options for the contract list.</summary>
public sealed class ContractSearchCriteria
{
    /// <summary>Show contracts whose term overlaps the range starting at this date.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Show contracts whose term overlaps the range ending at this date.</summary>
    public DateTime? ToDate { get; set; }

    public ContractStatus? Status { get; set; }
    public ServiceLevel? ServiceLevel { get; set; }
    public int? ClientId { get; set; }

    /// <summary>Matches contract number, title or client name (case-insensitive).</summary>
    public string? Keyword { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (FromDate.HasValue && ToDate.HasValue && FromDate.Value.Date > ToDate.Value.Date)
            errors.Add("The 'From' date must be on or before the 'To' date.");
        return errors;
    }

    public bool IsEmpty =>
        FromDate is null && ToDate is null && Status is null && ServiceLevel is null && ClientId is null &&
        string.IsNullOrWhiteSpace(Keyword);
}

/// <summary>
/// LINQ search/filter. Written against IQueryable so the SAME code is translated to SQL by
/// EF Core in the app, and runs over in-memory lists in the unit tests.
/// </summary>
public static class ContractQueryExtensions
{
    public static IQueryable<Contract> ApplySearch(this IQueryable<Contract> query, ContractSearchCriteria? criteria)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (criteria is null) return query;

        var errors = criteria.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(criteria));

        // Date range = OVERLAP: a contract running Jan-Dec appears in a search for "March".
        if (criteria.FromDate.HasValue)
        {
            var from = criteria.FromDate.Value.Date;
            query = query.Where(c => c.EndDate >= from);
        }

        if (criteria.ToDate.HasValue)
        {
            var to = criteria.ToDate.Value.Date;
            query = query.Where(c => c.StartDate <= to);
        }

        if (criteria.Status.HasValue)
        {
            var status = criteria.Status.Value;
            query = query.Where(c => c.Status == status);
        }

        if (criteria.ServiceLevel.HasValue)
        {
            var level = criteria.ServiceLevel.Value;
            query = query.Where(c => c.ServiceLevel == level);
        }

        if (criteria.ClientId.HasValue)
        {
            var clientId = criteria.ClientId.Value;
            query = query.Where(c => c.ClientId == clientId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Keyword))
        {
            var keyword = criteria.Keyword.Trim().ToLower();
            query = query.Where(c =>
                c.ContractNumber.ToLower().Contains(keyword) ||
                c.Title.ToLower().Contains(keyword) ||
                (c.Client != null && c.Client.Name.ToLower().Contains(keyword)));
        }

        return query
            .OrderByDescending(c => c.StartDate)
            .ThenBy(c => c.ContractNumber);
    }

    /// <summary>Contracts whose term has ended while still Active/On Hold.</summary>
    public static IQueryable<Contract> WhereOverdueForExpiry(this IQueryable<Contract> query, DateTime today)
    {
        var date = today.Date;
        return query.Where(c =>
            (c.Status == ContractStatus.Active || c.Status == ContractStatus.OnHold) && c.EndDate < date);
    }

    /// <summary>Contracts a Logistics Manager may raise requests against today.</summary>
    public static IQueryable<Contract> WhereOpenForRequests(this IQueryable<Contract> query, DateTime today)
    {
        var date = today.Date;
        return query.Where(c => c.Status == ContractStatus.Active && c.StartDate <= date && c.EndDate >= date);
    }
}
