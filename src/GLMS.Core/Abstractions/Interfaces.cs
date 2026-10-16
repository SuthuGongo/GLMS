using GLMS.Core.Domain;
using GLMS.Core.Querying;

namespace GLMS.Core.Abstractions;

/// <summary>Abstracts "now" so date-based rules (expiry, SLA due dates) are testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Today => DateTime.Today;
}

// ---------------------------------------------------------------- Repositories
// Controllers never touch the DbContext; they go through services, which use these.

public interface IClientRepository
{
    Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken ct = default);
    Task<Client?> GetByIdAsync(int id, bool includeContracts = false, CancellationToken ct = default);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
    Task<bool> HasContractsAsync(int id, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, int? excludingId = null, CancellationToken ct = default);
    Task AddAsync(Client client, CancellationToken ct = default);
    void Remove(Client client);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IContractRepository
{
    Task<IReadOnlyList<Contract>> SearchAsync(ContractSearchCriteria criteria, CancellationToken ct = default);
    Task<Contract?> GetByIdAsync(int id, bool includeRequests = false, CancellationToken ct = default);
    Task<IReadOnlyList<Contract>> GetOpenForRequestsAsync(DateTime today, CancellationToken ct = default);
    Task<IReadOnlyList<Contract>> GetOverdueForExpiryAsync(DateTime today, CancellationToken ct = default);
    Task<IReadOnlyDictionary<ContractStatus, int>> CountByStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContractAuditEntry>> GetAuditTrailAsync(int contractId, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);

    /// <summary>
    /// Tells the next save which version of the contract the user was looking at. If it has changed since,
    /// SaveChangesAsync throws <c>ConcurrencyException</c>.
    /// </summary>
    void SetExpectedVersion(Contract contract, byte[] rowVersion);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IServiceRequestRepository
{
    Task<IReadOnlyList<ServiceRequest>> GetAllAsync(ServiceRequestStatus? status = null, int? contractId = null, CancellationToken ct = default);
    Task<ServiceRequest?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<int> CountOpenAsync(CancellationToken ct = default);

    /// <summary>Returns the stored row for this published rate, adding it the first time it is used.</summary>
    Task<ExchangeRate> GetOrAddExchangeRateAsync(ExchangeRate rate, CancellationToken ct = default);

    Task AddAsync(ServiceRequest request, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Where the ComplianceLogger observer writes its audit entries.</summary>
public interface IAuditSink
{
    void Record(ContractAuditEntry entry);
}

// ---------------------------------------------------------------- Files

public sealed record StoredFile(string StoredFileName, string OriginalFileName, long SizeBytes);

/// <summary>Simulated file server for signed agreements.</summary>
public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, long declaredLength, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storedFileName, CancellationToken ct = default);
    bool Exists(string storedFileName);
    void Delete(string storedFileName);
}
