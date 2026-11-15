using System.Net;
using GLMS.IntegrationTests.Infrastructure;

namespace GLMS.IntegrationTests.Api;

/// <summary>/api/contracts: filtering, create-then-read, the status workflow, file upload and concurrency.</summary>
[Collection(ApiCollection.Name)]
public sealed class ContractsApiTests : ApiTestBase
{
    public ContractsApiTests(GlmsApiFixture api) : base(api) { }

    [Fact]
    public async Task GetContracts_Returns200_AndANonNullJsonList()
    {
        var response = await Client.GetAsync("api/contracts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var contracts = await ReadAsync<List<ContractDto>>(response);
        Assert.NotNull(contracts);
        Assert.NotEmpty(contracts); // the API seeds demo contracts on first start
        Assert.All(contracts, c => Assert.False(string.IsNullOrWhiteSpace(c.ContractNumber)));
    }

    [Fact]
    public async Task GetContracts_FilteredByStatus_ReturnsOnlyThatStatus()
    {
        var draft = await CreateDraftContractAsync();

        var drafts = await GetJsonAsync<List<ContractDto>>("api/contracts?status=Draft");

        Assert.NotEmpty(drafts);
        Assert.All(drafts, c => Assert.Equal(ContractStatus.Draft, c.Status));
        Assert.Contains(drafts, c => c.Id == draft.Id);
    }

    [Fact]
    public async Task GetContracts_FilteredByClientAndKeyword_FindsTheContract()
    {
        var created = await CreateDraftContractAsync();

        var results = await GetJsonAsync<List<ContractDto>>(
            $"api/contracts?clientId={created.ClientId}&keyword={Uri.EscapeDataString(created.Title)}");

        var only = Assert.Single(results);
        Assert.Equal(created.Id, only.Id);
    }

    [Fact]
    public async Task GetContracts_FilteredByDateRange_ReturnsOverlappingContracts()
    {
        var created = await CreateDraftContractAsync();
        var from = Today.AddDays(30).ToString("yyyy-MM-dd");
        var to = Today.AddDays(40).ToString("yyyy-MM-dd");

        var results = await GetJsonAsync<List<ContractDto>>($"api/contracts?fromDate={from}&toDate={to}&clientId={created.ClientId}");

        Assert.Contains(results, c => c.Id == created.Id);
        Assert.All(results, c => Assert.True(c.EndDate.Date >= Today.AddDays(30) && c.StartDate.Date <= Today.AddDays(40)));
    }

    [Fact]
    public async Task GetContracts_WithFromDateAfterToDate_Returns400()
    {
        var response = await Client.GetAsync("api/contracts?fromDate=2026-12-31&toDate=2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Contains(problem.AllErrors, e => e.Contains("'From' date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetContract_UnknownId_Returns404ProblemDetails()
    {
        var response = await Client.GetAsync("api/contracts/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Contains("987654", problem.Detail);
    }

    [Fact]
    public async Task CreateContract_ThenRead_ReturnsTheSameData()
    {
        // Data integrity: what is POSTed is what is stored and read back.
        var client = await CreateClientAsync();
        var request = NewContract(client.Id, ServiceLevel.PriorityLogistics);

        var post = await Client.PostAsync("api/contracts", Json(request));

        await AssertStatusAsync(HttpStatusCode.Created, post);
        Assert.NotNull(post.Headers.Location);
        var created = await ReadAsync<ContractDetailsDto>(post);

        var read = await GetJsonAsync<ContractDetailsDto>(post.Headers.Location!.ToString());

        Assert.Equal(created.Id, read.Id);
        Assert.Equal(request.Title, read.Title);
        Assert.Equal(client.Id, read.ClientId);
        Assert.Equal(client.Name, read.ClientName);
        Assert.Equal(request.StartDate.Date, read.StartDate.Date);
        Assert.Equal(request.EndDate.Date, read.EndDate.Date);
        Assert.Equal(ServiceLevel.PriorityLogistics, read.ServiceLevel);
        Assert.Equal(ContractStatus.Draft, read.Status);            // new contracts always start as Draft
        Assert.StartsWith("TM-", read.ContractNumber);
        Assert.False(read.HasSignedAgreement);
        Assert.Equal(new[] { ContractStatus.Active }, read.NextStatuses);
        Assert.NotNull(read.RowVersion);
    }

    [Fact]
    public async Task CreateContract_WithMissingTitle_Returns400WithFieldError()
    {
        var client = await CreateClientAsync();
        var request = NewContract(client.Id);
        request.Title = "";

        var response = await Client.PostAsync("api/contracts", Json(request));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.True(problem.Errors!.ContainsKey("Title"));
    }

    [Fact]
    public async Task CreateContract_EndDateBeforeStartDate_Returns400BusinessRule()
    {
        var client = await CreateClientAsync();
        var request = NewContract(client.Id);
        request.EndDate = request.StartDate.AddDays(-1);

        var response = await Client.PostAsync("api/contracts", Json(request));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Contains("The end date must be after the start date.", problem.AllErrors);
    }

    [Fact]
    public async Task CreateContract_ForUnknownClient_Returns400()
    {
        var response = await Client.PostAsync("api/contracts", Json(NewContract(clientId: 987654)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Choose an existing client.", (await ReadProblemAsync(response)).AllErrors);
    }

    [Fact]
    public async Task Activate_WithoutSignedAgreement_Returns400()
    {
        var draft = await CreateDraftContractAsync();

        var response = await PatchAsync(Client, $"api/contracts/{draft.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.Active });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("signed agreement", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ContractStatus.Draft, (await GetJsonAsync<ContractDetailsDto>($"api/contracts/{draft.Id}")).Status); // unchanged
    }

    [Fact]
    public async Task UploadAgreement_ThenActivate_Returns200_AndWritesTheAuditTrail()
    {
        var draft = await CreateDraftContractAsync();

        var upload = await UploadAgreementAsync(draft.Id, SamplePdf());
        await AssertStatusAsync(HttpStatusCode.OK, upload);
        Assert.True((await ReadAsync<ContractDetailsDto>(upload)).HasSignedAgreement);

        var activate = await PatchAsync(Client, $"api/contracts/{draft.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.Active });

        await AssertStatusAsync(HttpStatusCode.OK, activate);
        var active = await ReadAsync<ContractDetailsDto>(activate);
        Assert.Equal(ContractStatus.Active, active.Status);
        Assert.True(active.CanRaiseRequests);

        // Observer pattern: the ComplianceLogger recorded the change, persisted in SQL Server.
        var audit = await GetJsonAsync<List<AuditEntryDto>>($"api/contracts/{draft.Id}/audit");
        var entry = Assert.Single(audit);
        Assert.Equal(ContractStatus.Draft, entry.FromStatus);
        Assert.Equal(ContractStatus.Active, entry.ToStatus);
    }

    [Fact]
    public async Task UploadAgreement_ExeRenamedToPdf_Returns400_AndStoresNothing()
    {
        var draft = await CreateDraftContractAsync();
        var fakePdf = "MZ\u0090\0 this is really an executable"u8.ToArray();

        var response = await UploadAgreementAsync(draft.Id, fakePdf, "contract.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("not a valid PDF", StringComparison.Ordinal));
        Assert.False((await GetJsonAsync<ContractDetailsDto>($"api/contracts/{draft.Id}")).HasSignedAgreement);
    }

    [Fact]
    public async Task UploadAgreement_WithExeExtension_Returns400()
    {
        var draft = await CreateDraftContractAsync();

        var response = await UploadAgreementAsync(draft.Id, SamplePdf(), "invoice.exe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("Only PDF files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAgreement_ReturnsTheUploadedPdf()
    {
        var contract = await CreateActiveContractAsync();

        var response = await Client.GetAsync($"api/contracts/{contract.Id}/agreement");

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(SamplePdf().SequenceEqual(await response.Content.ReadAsByteArrayAsync()), "Downloaded bytes differ from the upload.");
    }

    [Fact]
    public async Task ExpiredContract_CannotBeReactivated_Returns400()
    {
        var contract = await CreateActiveContractAsync();
        await AssertStatusAsync(HttpStatusCode.OK,
            await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.Expired }));

        var response = await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.Active });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("cannot move from Expired to Active", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutOnHold_ThenReactivate_FollowsTheWorkflow()
    {
        var contract = await CreateActiveContractAsync();

        var hold = await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.OnHold });
        await AssertStatusAsync(HttpStatusCode.OK, hold);
        var onHold = await ReadAsync<ContractDetailsDto>(hold);
        Assert.Equal(ContractStatus.OnHold, onHold.Status);
        Assert.False(onHold.CanRaiseRequests);
        Assert.NotEmpty(onHold.RequestBlockers);

        var resume = await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.Active });
        await AssertStatusAsync(HttpStatusCode.OK, resume);
        Assert.Equal(ContractStatus.Active, (await ReadAsync<ContractDetailsDto>(resume)).Status);
    }

    [Fact]
    public async Task ChangeStatus_WithUnknownStatusValue_Returns400()
    {
        var draft = await CreateDraftContractAsync();
        var body = new StringContent("{\"status\":\"Approved-ish\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"api/contracts/{draft.Id}/status") { Content = body });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithCurrentRowVersion_Returns200_AndSavesTheChange()
    {
        var draft = await CreateDraftContractAsync();
        var update = NewContract(draft.ClientId);
        update.Title = draft.Title + " (renamed)";
        update.RowVersion = draft.RowVersion;

        var response = await Client.PutAsync($"api/contracts/{draft.Id}", Json(update));

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Equal(update.Title, (await GetJsonAsync<ContractDetailsDto>($"api/contracts/{draft.Id}")).Title);
    }

    [Fact]
    public async Task Update_WithStaleRowVersion_Returns409Conflict()
    {
        var draft = await CreateDraftContractAsync();
        var staleVersion = draft.RowVersion;

        // Someone else saves first...
        var first = NewContract(draft.ClientId);
        first.RowVersion = staleVersion;
        await AssertStatusAsync(HttpStatusCode.OK, await Client.PutAsync($"api/contracts/{draft.Id}", Json(first)));

        // ...then a save based on the old version must be rejected, not silently overwrite.
        var second = NewContract(draft.ClientId);
        second.RowVersion = staleVersion;
        var response = await Client.PutAsync($"api/contracts/{draft.Id}", Json(second));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains((await ReadProblemAsync(response)).AllErrors, e => e.Contains("Someone else changed", StringComparison.Ordinal));
        Assert.Equal(first.Title, (await GetJsonAsync<ContractDetailsDto>($"api/contracts/{draft.Id}")).Title);
    }

    [Fact]
    public async Task CreateWithAgreement_Multipart_CreatesAnActiveContractInOneStep()
    {
        var client = await CreateClientAsync();
        var pdf = new ByteArrayContent(SamplePdf());
        pdf.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent
        {
            { new StringContent(client.Id.ToString()), "ClientId" },
            { new StringContent(Unique("IT Multipart")), "Title" },
            { new StringContent(Today.ToString("yyyy-MM-dd")), "StartDate" },
            { new StringContent(Today.AddYears(1).ToString("yyyy-MM-dd")), "EndDate" },
            { new StringContent("Standard"), "ServiceLevel" },
            { new StringContent("true"), "ActivateNow" },
            { pdf, "SignedAgreement", "signed.pdf" }
        };

        var response = await Client.PostAsync("api/contracts/with-agreement", form);

        await AssertStatusAsync(HttpStatusCode.Created, response);
        var created = await ReadAsync<ContractDetailsDto>(response);
        Assert.Equal(ContractStatus.Active, created.Status);
        Assert.True(created.HasSignedAgreement);
    }
}
