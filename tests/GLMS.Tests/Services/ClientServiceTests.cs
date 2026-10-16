using GLMS.Core.Domain;
using GLMS.Core.Exceptions;
using GLMS.Core.Services;
using GLMS.Tests.TestDoubles;

namespace GLMS.Tests.Services;

public class ClientServiceTests
{
    private readonly FakeClientRepository _repo = new();
    private readonly ClientService _service;

    public ClientServiceTests() => _service = new ClientService(_repo, new FakeClock());

    [Fact]
    public async Task Create_ValidClient_TrimsAndSaves()
    {
        var client = await _service.CreateAsync(new ClientInput("  Atlantic Freight ", "Thandi", "thandi@atlantic.co.za", "021 555 0101", " Africa "));

        Assert.Equal("Atlantic Freight", client.Name);
        Assert.Equal("Africa", client.Region);
        Assert.Equal(1, _repo.Saves);
    }

    [Theory]
    [InlineData("", "a@b.com", "Africa")]
    [InlineData("Name", "not-an-email", "Africa")]
    [InlineData("Name", "a@b.com", "")]
    public async Task Create_InvalidInput_IsRejected(string name, string email, string region)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.CreateAsync(new ClientInput(name, "", email, "", region)));
        Assert.Empty(_repo.Clients);
    }

    [Fact]
    public async Task Delete_ClientWithContracts_IsRejected()
    {
        var client = new Client { Id = 1, Name = "A" };
        client.Contracts.Add(TestData.Contract());
        _repo.Clients.Add(client);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.DeleteAsync(1));
        Assert.Single(_repo.Clients);
    }

    [Fact]
    public async Task Delete_ClientWithoutContracts_IsRemoved()
    {
        _repo.Clients.Add(new Client { Id = 1, Name = "A" });
        await _service.DeleteAsync(1);
        Assert.Empty(_repo.Clients);
    }

    [Fact]
    public async Task Get_UnknownClient_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(42));
    }
}
