using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Querying;
using Microsoft.EntityFrameworkCore;

namespace GLMS.Web.Data;

/// <summary>
/// Saves through EF Core and turns database errors into messages the user can act on, so a clash that slips
/// past validation (two people editing at once, a duplicate value) never shows an error page.
/// </summary>
internal static class SafeSave
{
    public static async Task SaveAsync(GlmsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(
                "Someone else changed this contract after you opened it, so your change was not saved. Reload the page to see the latest version, then try again.");
        }
        catch (DbUpdateException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            var message = detail.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || detail.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                ? "That change would create a duplicate record (for example a client name that is already used), so it was not saved."
                : detail.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase)
                    ? "That change breaks a data rule (for example an end date before the start date), so it was not saved."
                    : "The change could not be saved. Nothing was changed; please check the details and try again.";
            throw new BusinessRuleException(message);
        }
    }
}

// Repository implementations: the ONLY classes (apart from the seeder) that touch GlmsDbContext.
// All are registered as Scoped, so in one HTTP request they share one DbContext (unit of work):
// a SaveChangesAsync() persists contract changes, observer side-effects and audit entries together.

public sealed class ClientRepository : IClientRepository
{
    private readonly GlmsDbContext _db;
    public ClientRepository(GlmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Clients.AsNoTracking()
            .Include(c => c.Contracts)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public Task<Client?> GetByIdAsync(int id, bool includeContracts = false, CancellationToken ct = default)
    {
        IQueryable<Client> query = _db.Clients;
        if (includeContracts) query = query.Include(c => c.Contracts);
        return query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public Task<bool> ExistsAsync(int id, CancellationToken ct = default) => _db.Clients.AnyAsync(c => c.Id == id, ct);

    public Task<bool> HasContractsAsync(int id, CancellationToken ct = default) => _db.Contracts.AnyAsync(c => c.ClientId == id, ct);

    public Task<bool> NameExistsAsync(string name, int? excludingId = null, CancellationToken ct = default) =>
        _db.Clients.AnyAsync(c => c.Name == name && (excludingId == null || c.Id != excludingId), ct);

    public async Task AddAsync(Client client, CancellationToken ct = default) => await _db.Clients.AddAsync(client, ct);

    public void Remove(Client client) => _db.Clients.Remove(client);

    public Task SaveChangesAsync(CancellationToken ct = default) => SafeSave.SaveAsync(_db, ct);
}

public sealed class ContractRepository : IContractRepository
{
    private readonly GlmsDbContext _db;
    public ContractRepository(GlmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<Contract>> SearchAsync(ContractSearchCriteria criteria, CancellationToken ct = default) =>
        await _db.Contracts.AsNoTracking()
            .Include(c => c.Client)
            .Include(c => c.ServiceRequests)
            .ApplySearch(criteria)          // LINQ filter from GLMS.Core, translated to SQL
            .ToListAsync(ct);

    public Task<Contract?> GetByIdAsync(int id, bool includeRequests = false, CancellationToken ct = default)
    {
        IQueryable<Contract> query = _db.Contracts.Include(c => c.Client);
        if (includeRequests) query = query.Include(c => c.ServiceRequests);
        return query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<Contract>> GetOpenForRequestsAsync(DateTime today, CancellationToken ct = default) =>
        await _db.Contracts.AsNoTracking()
            .Include(c => c.Client)
            .WhereOpenForRequests(today)
            .OrderBy(c => c.Client!.Name).ThenBy(c => c.ContractNumber)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Contract>> GetOverdueForExpiryAsync(DateTime today, CancellationToken ct = default) =>
        await _db.Contracts
            .Include(c => c.ServiceRequests)   // the validator observer cancels their open requests
            .WhereOverdueForExpiry(today)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<ContractStatus, int>> CountByStatusAsync(CancellationToken ct = default)
    {
        var counts = await _db.Contracts
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Enum.GetValues<ContractStatus>()
            .ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);
    }

    public async Task<IReadOnlyList<ContractAuditEntry>> GetAuditTrailAsync(int contractId, CancellationToken ct = default) =>
        await _db.ContractAuditEntries.AsNoTracking()
            .Where(a => a.ContractId == contractId)
            .OrderByDescending(a => a.OccurredUtc)
            .ToListAsync(ct);

    public async Task AddAsync(Contract contract, CancellationToken ct = default) => await _db.Contracts.AddAsync(contract, ct);

    public void SetExpectedVersion(Contract contract, byte[] rowVersion) =>
        // EF compares this with the row's current rowversion in the UPDATE's WHERE clause.
        _db.Entry(contract).Property(c => c.RowVersion).OriginalValue = rowVersion;

    public Task SaveChangesAsync(CancellationToken ct = default) => SafeSave.SaveAsync(_db, ct);
}

public sealed class ServiceRequestRepository : IServiceRequestRepository
{
    private readonly GlmsDbContext _db;
    public ServiceRequestRepository(GlmsDbContext db) => _db = db;

    public async Task<IReadOnlyList<ServiceRequest>> GetAllAsync(ServiceRequestStatus? status = null, int? contractId = null, CancellationToken ct = default)
    {
        IQueryable<ServiceRequest> query = _db.ServiceRequests.AsNoTracking()
            .Include(r => r.ExchangeRate)
            .Include(r => r.Contract).ThenInclude(c => c!.Client);

        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        if (contractId.HasValue) query = query.Where(r => r.ContractId == contractId.Value);

        return await query.OrderByDescending(r => r.CreatedUtc).ToListAsync(ct);
    }

    public Task<ServiceRequest?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.ServiceRequests
            .Include(r => r.ExchangeRate)
            .Include(r => r.Contract).ThenInclude(c => c!.Client)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<int> CountOpenAsync(CancellationToken ct = default) =>
        _db.ServiceRequests.CountAsync(r =>
            r.Status == ServiceRequestStatus.Pending ||
            r.Status == ServiceRequestStatus.InProgress ||
            r.Status == ServiceRequestStatus.OnHold, ct);

    public async Task<ExchangeRate> GetOrAddExchangeRateAsync(ExchangeRate rate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rate);
        var existing = await _db.ExchangeRates.FirstOrDefaultAsync(r =>
            r.BaseCurrency == rate.BaseCurrency && r.QuoteCurrency == rate.QuoteCurrency &&
            r.PublishedUtc == rate.PublishedUtc && r.Rate == rate.Rate, ct);
        if (existing is not null) return existing;

        await _db.ExchangeRates.AddAsync(rate, ct);   // saved together with the request
        return rate;
    }

    public async Task AddAsync(ServiceRequest request, CancellationToken ct = default) => await _db.ServiceRequests.AddAsync(request, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => SafeSave.SaveAsync(_db, ct);
}

/// <summary>Persists ComplianceLogger output in the same unit of work as the status change.</summary>
public sealed class EfAuditSink : IAuditSink
{
    private readonly GlmsDbContext _db;
    public EfAuditSink(GlmsDbContext db) => _db = db;

    public void Record(ContractAuditEntry entry) => _db.ContractAuditEntries.Add(entry);
}
