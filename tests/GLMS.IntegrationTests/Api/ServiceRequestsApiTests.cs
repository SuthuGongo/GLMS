using System.Net;
using GLMS.IntegrationTests.Infrastructure;

namespace GLMS.IntegrationTests.Api;

/// <summary>/api/service-requests: currency conversion (Strategy), SLA tiers (Factory Method) and contract rules (Observer).</summary>
[Collection(ApiCollection.Name)]
public sealed class ServiceRequestsApiTests : ApiTestBase
{
    public ServiceRequestsApiTests(GlmsApiFixture api) : base(api) { }

    [Fact]
    public async Task CreateRequest_OnActiveContract_Returns201_ConvertsUsdToZar_AndCanBeReadBack()
    {
        var contract = await CreateActiveContractAsync();

        var post = await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = contract.Id,
            Description = "Expedited customs clearance for one container",
            CostUsd = 1250.40m
        }));

        await AssertStatusAsync(HttpStatusCode.Created, post);
        Assert.NotNull(post.Headers.Location);
        var created = await ReadAsync<ServiceRequestDto>(post);

        Assert.True(created.ExchangeRateUsed > 0);
        Assert.Equal(Math.Round(1250.40m * created.ExchangeRateUsed, 2, MidpointRounding.AwayFromZero), created.CostZar);
        if (!Api.IsExternal) Assert.Equal(18.50m, created.ExchangeRateUsed); // fixed rate in the in-memory host

        var read = await GetJsonAsync<ServiceRequestDto>(post.Headers.Location!.ToString());
        Assert.Equal(created.Id, read.Id);
        Assert.Equal(1250.40m, read.CostUsd);
        Assert.Equal(created.CostZar, read.CostZar);
        Assert.Equal(contract.ContractNumber, read.ContractNumber);
        Assert.Equal(ServiceRequestStatus.Pending, read.Status);
        Assert.Equal(RequestPriority.Normal, read.Priority);
        Assert.True(read.ResponseDueUtc > read.CreatedUtc);
    }

    [Fact]
    public async Task CreateUrgentRequest_OnPriorityContract_GetsCriticalPriorityAndTwoHourResponse()
    {
        var contract = await CreateActiveContractAsync(ServiceLevel.PriorityLogistics);

        var post = await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = contract.Id,
            Description = "Urgent re-routing of a delayed shipment",
            CostUsd = 500m,
            IsUrgent = true
        }));

        await AssertStatusAsync(HttpStatusCode.Created, post);
        var created = await ReadAsync<ServiceRequestDto>(post);
        Assert.Equal(RequestPriority.Critical, created.Priority);
        Assert.Equal(TimeSpan.FromHours(2), created.ResponseDueUtc - created.CreatedUtc);
    }

    [Fact]
    public async Task CreateRequest_OnDraftContract_Returns400()
    {
        var draft = await CreateDraftContractAsync();

        var response = await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = draft.Id,
            Description = "Should be rejected",
            CostUsd = 10m
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("DRAFT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRequest_OnContractOnHold_Returns400()
    {
        var contract = await CreateActiveContractAsync();
        await AssertStatusAsync(HttpStatusCode.OK,
            await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.OnHold }));

        var response = await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = contract.Id,
            Description = "Should be rejected while on hold",
            CostUsd = 10m
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("ON HOLD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRequest_WithZeroCost_Returns400()
    {
        var contract = await CreateActiveContractAsync();

        var response = await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = contract.Id,
            Description = "Zero cost is not allowed",
            CostUsd = 0m
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadProblemAsync(response)).Errors!.ContainsKey("CostUsd"));
    }

    [Fact]
    public async Task RequestLifecycle_StartThenComplete_Returns200AtEachStep()
    {
        var contract = await CreateActiveContractAsync();
        var created = await ReadAsync<ServiceRequestDto>(await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = contract.Id,
            Description = "Pallet storage for two weeks",
            CostUsd = 300m
        })));

        var start = await PatchAsync(Client, $"api/service-requests/{created.Id}/status", new ChangeRequestStatusRequest { Status = ServiceRequestStatus.InProgress });
        await AssertStatusAsync(HttpStatusCode.OK, start);
        Assert.Equal(ServiceRequestStatus.InProgress, (await ReadAsync<ServiceRequestDto>(start)).Status);

        var complete = await PatchAsync(Client, $"api/service-requests/{created.Id}/status", new ChangeRequestStatusRequest { Status = ServiceRequestStatus.Completed });
        await AssertStatusAsync(HttpStatusCode.OK, complete);
        var done = await ReadAsync<ServiceRequestDto>(complete);
        Assert.Equal(ServiceRequestStatus.Completed, done.Status);
        Assert.NotNull(done.ClosedUtc);

        // A completed request cannot be completed again.
        var again = await PatchAsync(Client, $"api/service-requests/{created.Id}/status", new ChangeRequestStatusRequest { Status = ServiceRequestStatus.Completed });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task PuttingTheContractOnHold_PausesItsOpenRequests()
    {
        var contract = await CreateActiveContractAsync();
        var created = await ReadAsync<ServiceRequestDto>(await Client.PostAsync("api/service-requests", Json(new CreateServiceRequestRequest
        {
            ContractId = contract.Id,
            Description = "Request that should be paused",
            CostUsd = 75m
        })));

        await AssertStatusAsync(HttpStatusCode.OK,
            await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.OnHold }));

        // Observer pattern: ServiceRequestValidator reacted to the contract's status change.
        var paused = await GetJsonAsync<ServiceRequestDto>($"api/service-requests/{created.Id}");
        Assert.Equal(ServiceRequestStatus.OnHold, paused.Status);
    }

    [Fact]
    public async Task GetRequests_FilteredByStatus_ReturnsOnlyThatStatus()
    {
        var list = await GetJsonAsync<List<ServiceRequestDto>>("api/service-requests?status=Pending");

        Assert.All(list, r => Assert.Equal(ServiceRequestStatus.Pending, r.Status));
    }

    [Fact]
    public async Task GetRequest_UnknownId_Returns404()
    {
        var response = await Client.GetAsync("api/service-requests/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
