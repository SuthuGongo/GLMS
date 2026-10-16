using GLMS.Core.Abstractions;
using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Validation;

namespace GLMS.Core.Services;

public sealed record ClientInput(string Name, string ContactPerson, string Email, string PhoneNumber, string Region);

public sealed class ClientService
{
    private readonly IClientRepository _clients;
    private readonly IClock _clock;

    public ClientService(IClientRepository clients, IClock clock)
    {
        _clients = clients;
        _clock = clock;
    }

    public Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken ct = default) => _clients.GetAllAsync(ct);

    public async Task<Client> GetAsync(int id, CancellationToken ct = default) =>
        await _clients.GetByIdAsync(id, includeContracts: true, ct) ?? throw new NotFoundException($"Client {id} was not found.");

    public async Task<Client> CreateAsync(ClientInput input, CancellationToken ct = default)
    {
        Validate(input);
        await EnsureUniqueNameAsync(input.Name, null, ct);
        var client = new Client { CreatedUtc = _clock.UtcNow };
        Apply(client, input);
        await _clients.AddAsync(client, ct);
        await _clients.SaveChangesAsync(ct);
        return client;
    }

    public async Task<Client> UpdateAsync(int id, ClientInput input, CancellationToken ct = default)
    {
        Validate(input);
        var client = await _clients.GetByIdAsync(id, false, ct) ?? throw new NotFoundException($"Client {id} was not found.");
        await EnsureUniqueNameAsync(input.Name, id, ct);
        Apply(client, input);
        await _clients.SaveChangesAsync(ct);
        return client;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(id, false, ct) ?? throw new NotFoundException($"Client {id} was not found.");
        if (await _clients.HasContractsAsync(id, ct))
            throw new BusinessRuleException("This client has contracts on record and cannot be deleted (contracts are legal records).");
        _clients.Remove(client);
        await _clients.SaveChangesAsync(ct);
    }

    /// <summary>All field rules live in <see cref="ClientValidator"/>; this throws if any are broken.</summary>
    public static void Validate(ClientInput? input)
    {
        var errors = ClientValidator.Validate(input);
        if (errors.Count > 0) throw new BusinessRuleException(errors);
    }

    public static bool IsValidEmail(string? email) => ClientValidator.IsValidEmail(email);

    private async Task EnsureUniqueNameAsync(string name, int? excludingId, CancellationToken ct)
    {
        if (await _clients.NameExistsAsync(name.Trim(), excludingId, ct))
            throw new BusinessRuleException($"A client called '{name.Trim()}' already exists.");
    }

    private static void Apply(Client client, ClientInput input)
    {
        client.Name = input.Name.Trim();
        client.ContactPerson = (input.ContactPerson ?? string.Empty).Trim();
        client.Email = input.Email.Trim();
        client.PhoneNumber = (input.PhoneNumber ?? string.Empty).Trim();
        client.Region = input.Region.Trim();
    }
}
