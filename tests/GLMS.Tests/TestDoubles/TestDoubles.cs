using System.Net;
using System.Text;
using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Patterns.Observer;
using GLMS.Core.Patterns.Strategy;
using GLMS.Core.Querying;

namespace GLMS.Tests.TestDoubles;
public sealed class FakeClock : IClock
{
    public FakeClock(DateTime? utcNow = null) => UtcNow = utcNow ?? new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    public DateTime UtcNow { get; set; }
    public DateTime Today => UtcNow.Date;
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
public sealed class StubRateStrategy : ICurrencyConversionStrategy
{
    private readonly Queue<Func<ExchangeRateQuote>> _responses = new();
    private readonly decimal _defaultRate;

    public StubRateStrategy(decimal rate = 18.50m, string name = "Stub") { _defaultRate = rate; Name = name; }

    public string Name { get; }
    public int Calls { get; private set; }

    public StubRateStrategy ThenReturns(decimal rate) { _responses.Enqueue(() => Quote(rate)); return this; }
    public StubRateStrategy ThenFails() { _responses.Enqueue(() => throw new CurrencyConversionException("Service down")); return this; }

    public Task<ExchangeRateQuote> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        Calls++;
        var next = _responses.Count > 0 ? _responses.Dequeue() : () => Quote(_defaultRate);
        return Task.FromResult(next() with { FromCurrency = fromCurrency, ToCurrency = toCurrency });
    }

    private ExchangeRateQuote Quote(decimal rate) =>
        new("USD", "ZAR", rate, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), Name);
}

/// <summary>Fake HTTP handler so LiveRateApiStrategy can be tested without the internet.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<Uri> RequestedUris { get; } = new();

    public static FakeHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public static FakeHttpMessageHandler Throws(Exception ex) => new(_ => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri!);
        return Task.FromResult(_respond(request));
    }
}

public sealed class InMemoryAuditSink : IAuditSink
{
    public List<ContractAuditEntry> Entries { get; } = new();
    public void Record(ContractAuditEntry entry) => Entries.Add(entry);
}

/// <summary>Records every notification it receives (proves the Observer wiring).</summary>
public sealed class RecordingObserver : IContractObserver
{
    public List<(ContractStatus From, ContractStatus To)> Notifications { get; } = new();
    public void OnStatusChanged(Contract contract, ContractStatus previousStatus) =>
        Notifications.Add((previousStatus, contract.Status));
}

// ---------------------------------------------------------------- In-memory repositories

public sealed class FakeClientRepository : IClientRepository
{
    public List<Client> Clients { get; } = new();
    public int Saves { get; private set; }

    public Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Client>>(Clients.ToList());
    public Task<Client?> GetByIdAsync(int id, bool includeContracts = false, CancellationToken ct = default) => Task.FromResult(Clients.FirstOrDefault(c => c.Id == id));
    public Task<bool> ExistsAsync(int id, CancellationToken ct = default) => Task.FromResult(Clients.Any(c => c.Id == id));
    public Task<bool> HasContractsAsync(int id, CancellationToken ct = default) => Task.FromResult(Clients.First(c => c.Id == id).Contracts.Any());
    public Task<bool> NameExistsAsync(string name, int? excludingId = null, CancellationToken ct = default) =>
        Task.FromResult(Clients.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) && c.Id != excludingId));
    public Task AddAsync(Client client, CancellationToken ct = default) { client.Id = Clients.Count + 1; Clients.Add(client); return Task.CompletedTask; }
    public void Remove(Client client) => Clients.Remove(client);
    public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
}

public sealed class FakeContractRepository : IContractRepository
{
    public List<Contract> Contracts { get; } = new();
    public int Saves { get; private set; }

    public Task<IReadOnlyList<Contract>> SearchAsync(ContractSearchCriteria criteria, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Contract>>(Contracts.AsQueryable().ApplySearch(criteria).ToList());
    public Task<Contract?> GetByIdAsync(int id, bool includeRequests = false, CancellationToken ct = default) => Task.FromResult(Contracts.FirstOrDefault(c => c.Id == id));
    public Task<IReadOnlyList<Contract>> GetOpenForRequestsAsync(DateTime today, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Contract>>(Contracts.AsQueryable().WhereOpenForRequests(today).ToList());
    public Task<IReadOnlyList<Contract>> GetOverdueForExpiryAsync(DateTime today, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Contract>>(Contracts.AsQueryable().WhereOverdueForExpiry(today).ToList());
    public Task<IReadOnlyDictionary<ContractStatus, int>> CountByStatusAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<ContractStatus, int>>(Contracts.GroupBy(c => c.Status).ToDictionary(g => g.Key, g => g.Count()));
    public Task<IReadOnlyList<ContractAuditEntry>> GetAuditTrailAsync(int contractId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ContractAuditEntry>>(Array.Empty<ContractAuditEntry>());
    public Task AddAsync(Contract contract, CancellationToken ct = default) { contract.Id = Contracts.Count + 1; Contracts.Add(contract); return Task.CompletedTask; }

    // Simulates SQL Server rowversion: each save bumps the version; a stale expected version fails the save.
    private readonly List<(Contract Contract, byte[] Expected)> _expected = new();
    public void SetExpectedVersion(Contract contract, byte[] rowVersion) => _expected.Add((contract, rowVersion));

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var (contract, expected) in _expected)
            if (!(contract.RowVersion ?? Array.Empty<byte>()).SequenceEqual(expected))
            {
                _expected.Clear();
                throw new GLMS.Core.Exceptions.ConcurrencyException("Someone else changed this contract after you opened it.");
            }
        _expected.Clear();
        foreach (var c in Contracts) c.RowVersion = BitConverter.GetBytes((BitConverter.ToInt64(c.RowVersion is { Length: 8 } v ? v : new byte[8]) + 1));
        Saves++;
        return Task.CompletedTask;
    }
}

public sealed class FakeServiceRequestRepository : IServiceRequestRepository
{
    public List<ServiceRequest> Requests { get; } = new();
    public int Saves { get; private set; }

    public Task<IReadOnlyList<ServiceRequest>> GetAllAsync(ServiceRequestStatus? status = null, int? contractId = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ServiceRequest>>(Requests.Where(r => (status == null || r.Status == status) && (contractId == null || r.ContractId == contractId)).ToList());
    public Task<ServiceRequest?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Requests.FirstOrDefault(r => r.Id == id));
    public Task<int> CountOpenAsync(CancellationToken ct = default) => Task.FromResult(Requests.Count(r => r.IsOpen));

    public List<ExchangeRate> Rates { get; } = new();
    public Task<ExchangeRate> GetOrAddExchangeRateAsync(ExchangeRate rate, CancellationToken ct = default)
    {
        var existing = Rates.FirstOrDefault(r => r.IsSameRateAs(rate));
        if (existing is not null) return Task.FromResult(existing);
        rate.Id = Rates.Count + 1;
        Rates.Add(rate);
        return Task.FromResult(rate);
    }
    public Task AddAsync(ServiceRequest request, CancellationToken ct = default) { request.Id = Requests.Count + 1; Requests.Add(request); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
}

/// <summary>In-memory IFileStorageService that still applies the real FileValidator rules.</summary>
public sealed class FakeFileStorage : IFileStorageService
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public List<string> Deleted { get; } = new();

    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, long declaredLength, CancellationToken ct = default)
    {
        GLMS.Core.Files.FileValidator.ValidateMetadata(originalFileName, declaredLength);
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        GLMS.Core.Files.FileValidator.ValidatePdfSignature(ms.ToArray());
        GLMS.Core.Files.FileValidator.ValidatePdfTrailer(ms.ToArray());
        var name = $"{Guid.NewGuid():N}.pdf";
        Files[name] = ms.ToArray();
        return new StoredFile(name, originalFileName, ms.Length);
    }

    public Task<Stream> OpenReadAsync(string storedFileName, CancellationToken ct = default) =>
        Files.TryGetValue(storedFileName, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new NotFoundException("missing");

    public bool Exists(string storedFileName) => Files.ContainsKey(storedFileName);
    public void Delete(string storedFileName) { Files.Remove(storedFileName); Deleted.Add(storedFileName); }
}
