using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GLMS.IntegrationTests.Infrastructure;

/// <summary>Helpers shared by the API test classes: JSON in/out, test-data builders and problem-details checks.</summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiTestBase(GlmsApiFixture api) => Api = api;

    protected GlmsApiFixture Api { get; }

    /// <summary>Signed in as admin. Created per test class.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>"Today" as the API sees it (taken from GET /api/dashboard), so date-based tests match the server.</summary>
    protected DateTime Today { get; private set; }

    public virtual async Task InitializeAsync()
    {
        Client = await Api.CreateClientAsync();
        Today = (await GetJsonAsync<DashboardDto>("api/dashboard")).Today.Date;
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ JSON helpers

    protected static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body, body.GetType(), GlmsJson.Options), Encoding.UTF8, "application/json");

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(GlmsJson.Options);
        Assert.NotNull(value);
        return value!;
    }

    protected async Task<T> GetJsonAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadAsync<T>(response);
    }

    protected static Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url) { Content = Json(body) });

    /// <summary>Asserts the status code and shows the response body in the failure message.</summary>
    protected static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Reads an RFC 7807 problem-details body and checks it carries a message.</summary>
    protected static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadAsync<ProblemBody>(response);
        Assert.Equal((int)response.StatusCode, problem.Status);
        return problem;
    }

    protected sealed class ProblemBody
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public int Status { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }

        public IEnumerable<string> AllErrors => Errors?.Values.SelectMany(v => v) ?? Enumerable.Empty<string>();
    }

    // ------------------------------------------------------------------ Test data (each test makes its own, so tests never depend on each other)

    protected static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 60)];

    protected async Task<ClientDetailsDto> CreateClientAsync(string? name = null)
    {
        var response = await Client.PostAsync("api/clients", Json(new SaveClientRequest
        {
            Name = name ?? Unique("IT Client"),
            ContactPerson = "Test Person",
            Email = "it@example.co.za",
            PhoneNumber = "+27 21 555 0000",
            Region = "Southern Africa"
        }));
        await AssertStatusAsync(HttpStatusCode.Created, response);
        return await ReadAsync<ClientDetailsDto>(response);
    }

    protected SaveContractRequest NewContract(int clientId, ServiceLevel level = ServiceLevel.Standard) => new()
    {
        ClientId = clientId,
        Title = Unique("IT Contract"),
        StartDate = Today.AddDays(-10),
        EndDate = Today.AddDays(180),
        ServiceLevel = level
    };

    protected async Task<ContractDetailsDto> CreateDraftContractAsync(ServiceLevel level = ServiceLevel.Standard)
    {
        var client = await CreateClientAsync();
        var response = await Client.PostAsync("api/contracts", Json(NewContract(client.Id, level)));
        await AssertStatusAsync(HttpStatusCode.Created, response);
        return await ReadAsync<ContractDetailsDto>(response);
    }

    /// <summary>A minimal but valid PDF (header + %%EOF trailer), as the API's file validator requires.</summary>
    protected static byte[] SamplePdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

    protected async Task<HttpResponseMessage> UploadAgreementAsync(int contractId, byte[] content, string fileName = "signed-agreement.pdf")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await Client.PutAsync($"api/contracts/{contractId}/agreement", form);
    }

    protected async Task<ContractDetailsDto> CreateActiveContractAsync(ServiceLevel level = ServiceLevel.Standard)
    {
        var contract = await CreateDraftContractAsync(level);
        await AssertStatusAsync(HttpStatusCode.OK, await UploadAgreementAsync(contract.Id, SamplePdf()));
        var response = await PatchAsync(Client, $"api/contracts/{contract.Id}/status", new ChangeContractStatusRequest { Status = ContractStatus.Active });
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadAsync<ContractDetailsDto>(response);
    }
}
