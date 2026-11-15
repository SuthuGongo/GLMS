using System.Net;
using GLMS.IntegrationTests.Infrastructure;

namespace GLMS.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public sealed class ClientsApiTests : ApiTestBase
{
    public ClientsApiTests(GlmsApiFixture api) : base(api) { }

    [Fact]
    public async Task CreateClient_ThenRead_ReturnsTheSameData()
    {
        var request = new SaveClientRequest
        {
            Name = Unique("IT Freight"),
            ContactPerson = "Naledi Dlamini",
            Email = "naledi@itfreight.co.za",
            PhoneNumber = "+27 11 555 0199",
            Region = "Southern Africa"
        };

        var post = await Client.PostAsync("api/clients", Json(request));

        await AssertStatusAsync(HttpStatusCode.Created, post);
        var read = await GetJsonAsync<ClientDetailsDto>(post.Headers.Location!.ToString());
        Assert.Equal(request.Name, read.Name);
        Assert.Equal(request.ContactPerson, read.ContactPerson);
        Assert.Equal(request.Email, read.Email);
        Assert.Equal(request.PhoneNumber, read.PhoneNumber);
        Assert.Equal(request.Region, read.Region);
        Assert.Empty(read.Contracts);
    }

    [Fact]
    public async Task GetClients_IncludesANewClient_WithItsContractCount()
    {
        var contract = await CreateDraftContractAsync();

        var clients = await GetJsonAsync<List<ClientDto>>("api/clients");

        var mine = Assert.Single(clients, c => c.Id == contract.ClientId);
        Assert.Equal(1, mine.ContractCount);
    }

    [Fact]
    public async Task CreateClient_WithDuplicateName_Returns400()
    {
        var existing = await CreateClientAsync();

        var response = await Client.PostAsync("api/clients", Json(new SaveClientRequest
        {
            Name = existing.Name,
            Email = "other@example.com",
            Region = "Europe"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateClient_WithInvalidEmail_Returns400()
    {
        var response = await Client.PostAsync("api/clients", Json(new SaveClientRequest
        {
            Name = Unique("IT Bad Email"),
            Email = "not-an-email",
            Region = "Europe"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateClient_Returns200_AndSavesTheChange()
    {
        var client = await CreateClientAsync();
        var update = new SaveClientRequest { Name = client.Name, Email = "new@example.co.za", Region = "Europe" };

        var response = await Client.PutAsync($"api/clients/{client.Id}", Json(update));

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var read = await GetJsonAsync<ClientDetailsDto>($"api/clients/{client.Id}");
        Assert.Equal("new@example.co.za", read.Email);
        Assert.Equal("Europe", read.Region);
    }

    [Fact]
    public async Task DeleteClient_AsLogisticsManager_Returns403()
    {
        var client = await CreateClientAsync();
        using var manager = await Api.CreateClientAsync(GlmsApiFixture.ManagerUser);

        var response = await manager.DeleteAsync($"api/clients/{client.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync($"api/clients/{client.Id}")); // still there
    }

    [Fact]
    public async Task DeleteClient_AsAdmin_Returns204_ThenTheClientIsGone()
    {
        var client = await CreateClientAsync();

        var response = await Client.DeleteAsync($"api/clients/{client.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"api/clients/{client.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteClient_WithContracts_Returns400()
    {
        var contract = await CreateDraftContractAsync();

        var response = await Client.DeleteAsync($"api/clients/{contract.ClientId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("contracts on record", StringComparison.Ordinal));
    }
}
