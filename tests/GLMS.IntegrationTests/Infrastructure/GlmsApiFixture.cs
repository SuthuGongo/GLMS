using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GLMS.IntegrationTests.Infrastructure;

/// <summary>
/// Starts (or connects to) ONE running GLMS API for the whole test run and hands out HttpClients for it.
/// <list type="bullet">
///   <item>GLMS_API_BASE_URL set: talk to that already-running API (e.g. the Docker Compose container).</item>
///   <item>Otherwise: start SQL Server in Docker (Testcontainers) and host the real API in memory against it.</item>
/// </list>
/// Either way, every test goes through real HTTP, routing, JWT authentication, controllers, services, EF Core and SQL Server.
/// </summary>
public sealed class GlmsApiFixture : IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string ManagerUser = "manager";

    private IApiHost? _host;

    public string AdminPassword { get; } = Environment.GetEnvironmentVariable("GLMS_ADMIN_PASSWORD") ?? "Admin@12345";
    public string ManagerPassword { get; } = Environment.GetEnvironmentVariable("GLMS_MANAGER_PASSWORD") ?? "Manager@12345";

    /// <summary>True when the tests run against an external API (live exchange rates, shared database).</summary>
    public bool IsExternal { get; private set; }

    public async Task InitializeAsync()
    {
        var baseUrl = Environment.GetEnvironmentVariable("GLMS_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            _host = new ExternalApiHost(baseUrl);
            IsExternal = true;
        }
        else
        {
            _host = new InMemoryApiHost();
        }

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }

    /// <summary>A client with no token (for 401 tests and the login endpoint).</summary>
    public HttpClient CreateAnonymousClient() => _host!.CreateClient();

    /// <summary>Signs in through POST /api/auth/login and returns a client that sends the JWT on every request.</summary>
    public async Task<HttpClient> CreateClientAsync(string username = AdminUser)
    {
        var password = username == AdminUser ? AdminPassword : ManagerPassword;
        var client = CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest { Username = username, Password = password }, GlmsJson.Options);
        Assert.True(response.IsSuccessStatusCode, $"Login as '{username}' failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(GlmsJson.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }
}

/// <summary>Where the API under test runs.</summary>
public interface IApiHost : IAsyncDisposable
{
    Task StartAsync();
    HttpClient CreateClient();
}

/// <summary>An API that is already running, e.g. http://localhost:5080 from docker compose.</summary>
public sealed class ExternalApiHost : IApiHost
{
    private readonly Uri _baseAddress;

    public ExternalApiHost(string baseUrl) => _baseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    public async Task StartAsync()
    {
        // Wait (up to ~2 minutes) for the container to report healthy, so the suite can start right after "docker compose up".
        using var probe = CreateClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if ((await probe.GetAsync("health")).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException)
            {
                // not listening yet
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new InvalidOperationException($"The API at {_baseAddress} did not become healthy.");
    }

    public HttpClient CreateClient() => new() { BaseAddress = _baseAddress, Timeout = TimeSpan.FromSeconds(60) };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<GlmsApiFixture>
{
    /// <summary>All API test classes share one running API (and one database), started once.</summary>
    public const string Name = "GLMS API";
}
