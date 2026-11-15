using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GLMS.Shared;
using GLMS.Shared.Dtos;

namespace GLMS.Web.ApiClients;

// =====================================================================================================
// The front end's SERVICE LAYER (Part 3). The MVC controllers depend on these interfaces only; the
// implementations call GLMS.Api over HTTP with HttpClient. There is no DbContext, no connection string and
// no reference to GLMS.Core in this project: all data and business rules live behind the API.
// =====================================================================================================

public interface IAuthApi
{
    /// <summary>Returns null when the username or password is wrong.</summary>
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public interface IClientsApi
{
    Task<IReadOnlyList<ClientDto>> GetAllAsync(CancellationToken ct = default);
    Task<ClientDetailsDto> GetAsync(int id, CancellationToken ct = default);
    Task<ClientDetailsDto> CreateAsync(SaveClientRequest request, CancellationToken ct = default);
    Task<ClientDetailsDto> UpdateAsync(int id, SaveClientRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IContractsApi
{
    Task<IReadOnlyList<ContractDto>> SearchAsync(ContractSearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<ContractDto>> GetOpenForRequestsAsync(CancellationToken ct = default);
    Task<ContractDetailsDto> GetAsync(int id, CancellationToken ct = default);
    Task<ContractDetailsDto> CreateAsync(SaveContractRequest request, AgreementFile? agreement, bool activateNow, CancellationToken ct = default);
    Task<ContractDetailsDto> UpdateAsync(int id, SaveContractRequest request, CancellationToken ct = default);
    Task<ContractDetailsDto> ChangeStatusAsync(int id, ContractStatus status, byte[]? rowVersion, CancellationToken ct = default);
    Task<ContractDetailsDto> UploadAgreementAsync(int id, AgreementFile file, CancellationToken ct = default);
    Task<DownloadedFile> DownloadAgreementAsync(int id, CancellationToken ct = default);
}

public interface IServiceRequestsApi
{
    Task<IReadOnlyList<ServiceRequestDto>> GetAllAsync(ServiceRequestStatus? status = null, CancellationToken ct = default);
    Task<ServiceRequestDto> GetAsync(int id, CancellationToken ct = default);
    Task<ServiceRequestDto> CreateAsync(CreateServiceRequestRequest request, CancellationToken ct = default);
    Task<ServiceRequestDto> ChangeStatusAsync(int id, ServiceRequestStatus status, string? reason = null, CancellationToken ct = default);
}

public interface IRatesApi
{
    Task<RateQuoteDto> GetUsdToZarAsync(CancellationToken ct = default);
}

public interface IDashboardApi
{
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}

/// <summary>An uploaded PDF on its way to the API.</summary>
public sealed record AgreementFile(Stream Content, string FileName);

public sealed record DownloadedFile(byte[] Content, string FileName, string ContentType);

// ------------------------------------------------------------------ Implementations

public sealed class AuthApi : ApiClientBase, IAuthApi
{
    public AuthApi(HttpClient http) : base(http) { }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        try
        {
            return await PostAsync<LoginResponse>("api/auth/login", request, ct);
        }
        catch (ApiUnauthorizedException)
        {
            return null;
        }
    }
}

public sealed class ClientsApi : ApiClientBase, IClientsApi
{
    public ClientsApi(HttpClient http) : base(http) { }

    public async Task<IReadOnlyList<ClientDto>> GetAllAsync(CancellationToken ct = default) =>
        await GetAsync<List<ClientDto>>("api/clients", ct);

    public Task<ClientDetailsDto> GetAsync(int id, CancellationToken ct = default) =>
        GetAsync<ClientDetailsDto>($"api/clients/{id}", ct);

    public Task<ClientDetailsDto> CreateAsync(SaveClientRequest request, CancellationToken ct = default) =>
        PostAsync<ClientDetailsDto>("api/clients", request, ct);

    public Task<ClientDetailsDto> UpdateAsync(int id, SaveClientRequest request, CancellationToken ct = default) =>
        PutAsync<ClientDetailsDto>($"api/clients/{id}", request, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default) => DeleteAsync($"api/clients/{id}", ct);
}

public sealed class ContractsApi : ApiClientBase, IContractsApi
{
    public ContractsApi(HttpClient http) : base(http) { }

    public async Task<IReadOnlyList<ContractDto>> SearchAsync(ContractSearchQuery query, CancellationToken ct = default) =>
        await GetAsync<List<ContractDto>>("api/contracts" + QueryString(query), ct);

    public async Task<IReadOnlyList<ContractDto>> GetOpenForRequestsAsync(CancellationToken ct = default) =>
        await GetAsync<List<ContractDto>>("api/contracts/open-for-requests", ct);

    public Task<ContractDetailsDto> GetAsync(int id, CancellationToken ct = default) =>
        GetAsync<ContractDetailsDto>($"api/contracts/{id}", ct);

    public Task<ContractDetailsDto> CreateAsync(SaveContractRequest request, AgreementFile? agreement, bool activateNow, CancellationToken ct = default)
    {
        // No file: plain JSON (POST /api/contracts). With a file: one multipart request, so the contract,
        // its PDF and the optional activation succeed or fail together on the server.
        if (agreement is null && !activateNow)
            return PostAsync<ContractDetailsDto>("api/contracts", request, ct);

        var form = new MultipartFormDataContent
        {
            { new StringContent(request.ClientId.ToString(CultureInfo.InvariantCulture)), nameof(SaveContractRequest.ClientId) },
            { new StringContent(request.Title), nameof(SaveContractRequest.Title) },
            { new StringContent(request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), nameof(SaveContractRequest.StartDate) },
            { new StringContent(request.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), nameof(SaveContractRequest.EndDate) },
            { new StringContent(request.ServiceLevel.ToString()), nameof(SaveContractRequest.ServiceLevel) },
            { new StringContent(activateNow ? "true" : "false"), "ActivateNow" }
        };
        if (agreement is not null)
            form.Add(PdfContent(agreement), "SignedAgreement", agreement.FileName);

        return SendAsync<ContractDetailsDto>(new HttpRequestMessage(HttpMethod.Post, "api/contracts/with-agreement") { Content = form }, ct);
    }

    public Task<ContractDetailsDto> UpdateAsync(int id, SaveContractRequest request, CancellationToken ct = default) =>
        PutAsync<ContractDetailsDto>($"api/contracts/{id}", request, ct);

    public Task<ContractDetailsDto> ChangeStatusAsync(int id, ContractStatus status, byte[]? rowVersion, CancellationToken ct = default) =>
        PatchAsync<ContractDetailsDto>($"api/contracts/{id}/status", new ChangeContractStatusRequest { Status = status, RowVersion = rowVersion }, ct);

    public Task<ContractDetailsDto> UploadAgreementAsync(int id, AgreementFile file, CancellationToken ct = default)
    {
        var form = new MultipartFormDataContent { { PdfContent(file), "file", file.FileName } };
        return SendAsync<ContractDetailsDto>(new HttpRequestMessage(HttpMethod.Put, $"api/contracts/{id}/agreement") { Content = form }, ct);
    }

    public async Task<DownloadedFile> DownloadAgreementAsync(int id, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(new HttpRequestMessage(HttpMethod.Get, $"api/contracts/{id}/agreement"), ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct); // at most 10 MB
        var disposition = response.Content.Headers.ContentDisposition;
        var name = (disposition?.FileNameStar ?? disposition?.FileName ?? $"contract-{id}-agreement.pdf").Trim('"');
        return new DownloadedFile(bytes, name, response.Content.Headers.ContentType?.MediaType ?? "application/pdf");
    }

    private static StreamContent PdfContent(AgreementFile file)
    {
        var content = new StreamContent(file.Content);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return content;
    }

    internal static string QueryString(ContractSearchQuery q)
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }

        Add("fromDate", q.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("toDate", q.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("status", q.Status?.ToString());
        Add("serviceLevel", q.ServiceLevel?.ToString());
        Add("clientId", q.ClientId?.ToString(CultureInfo.InvariantCulture));
        Add("keyword", q.Keyword?.Trim());
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}

public sealed class ServiceRequestsApi : ApiClientBase, IServiceRequestsApi
{
    public ServiceRequestsApi(HttpClient http) : base(http) { }

    public async Task<IReadOnlyList<ServiceRequestDto>> GetAllAsync(ServiceRequestStatus? status = null, CancellationToken ct = default) =>
        await GetAsync<List<ServiceRequestDto>>("api/service-requests" + (status.HasValue ? $"?status={status}" : string.Empty), ct);

    public Task<ServiceRequestDto> GetAsync(int id, CancellationToken ct = default) =>
        GetAsync<ServiceRequestDto>($"api/service-requests/{id}", ct);

    public Task<ServiceRequestDto> CreateAsync(CreateServiceRequestRequest request, CancellationToken ct = default) =>
        PostAsync<ServiceRequestDto>("api/service-requests", request, ct);

    public Task<ServiceRequestDto> ChangeStatusAsync(int id, ServiceRequestStatus status, string? reason = null, CancellationToken ct = default) =>
        PatchAsync<ServiceRequestDto>($"api/service-requests/{id}/status", new ChangeRequestStatusRequest { Status = status, Reason = reason }, ct);
}

public sealed class RatesApi : ApiClientBase, IRatesApi
{
    public RatesApi(HttpClient http) : base(http) { }

    public Task<RateQuoteDto> GetUsdToZarAsync(CancellationToken ct = default) => GetAsync<RateQuoteDto>("api/rates/usd-zar", ct);
}

public sealed class DashboardApi : ApiClientBase, IDashboardApi
{
    public DashboardApi(HttpClient http) : base(http) { }

    public Task<DashboardDto> GetAsync(CancellationToken ct = default) => GetAsync<DashboardDto>("api/dashboard", ct);
}
