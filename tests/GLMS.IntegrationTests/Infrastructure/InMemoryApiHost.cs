using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Testcontainers.MsSql;

namespace GLMS.IntegrationTests.Infrastructure;

/// <summary>
/// Real SQL Server 2022 in a throw-away Docker container (Testcontainers) + the real GLMS API hosted in memory
/// (WebApplicationFactory&lt;Program&gt;). The API runs its EF Core migrations and seed data against the container
/// exactly as it does in docker compose. Requires Docker to be running.
/// </summary>
public sealed class InMemoryApiHost : IApiHost
{
    // Same image as docker-compose.yml. Ready once SQL Server logs that it accepts connections;
    // the API's start-up also retries until the database answers.
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Glms_Test_Passw0rd!")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready for client connections"))
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task StartAsync()
    {
        await _sql.StartAsync();

        var connectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "GLMS_IntegrationTests",
            TrustServerCertificate = true
        }.ConnectionString;

        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:GlmsDatabase"] = connectionString,
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789-abcdefghij",
            ["CurrencyApi:Mode"] = "Fixed",           // deterministic USD -> ZAR 18.50, no internet needed
            ["ContractExpiry:Enabled"] = "false",     // no background changes while tests run
            ["FileStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "glms-it-" + Guid.NewGuid().ToString("N"))
        };

        // Program.cs reads these while registering services, so they are also set as environment variables
        // (always visible to WebApplication.CreateBuilder).
        foreach (var (key, value) in settings)
            Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });

        // Creating the first client starts the API (migrations + seed run here).
        using var _ = _factory.CreateClient();
    }

    public HttpClient CreateClient() => _factory!.CreateClient();

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _sql.DisposeAsync();
    }
}