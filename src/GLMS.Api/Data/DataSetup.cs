using GLMS.Api.Auth;
using GLMS.Core.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GLMS.Api.Data;

/// <summary>Registers EF Core and the repositories. The API is the ONLY service with a connection string.</summary>
public static class DataSetup
{
    public static IServiceCollection AddGlmsData(this IServiceCollection services, IConfiguration configuration)
    {
        // In Docker Compose this comes from the environment variable ConnectionStrings__GlmsDatabase.
        var connectionString = configuration.GetConnectionString("GlmsDatabase")
            ?? throw new InvalidOperationException("Connection string 'GlmsDatabase' is missing (appsettings.json or ConnectionStrings__GlmsDatabase).");

        services.AddDbContext<GlmsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5)));

        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IServiceRequestRepository, ServiceRequestRepository>();
        services.AddScoped<IAuditSink, EfAuditSink>();
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        return services;
    }
}

/// <summary>/health reports Unhealthy until SQL Server answers (used by the Docker health check).</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly GlmsDbContext _db;
    public DatabaseHealthCheck(GlmsDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await _db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy("SQL Server reachable")
            : HealthCheckResult.Unhealthy("SQL Server not reachable");
}

/// <summary>
/// Applies migrations and seeds demo data at start-up. In containers SQL Server can take a while to accept
/// connections, so this retries for up to about two minutes instead of crashing the API.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        const int maxAttempts = 24;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var db = sp.GetRequiredService<GlmsDbContext>();

                if (!db.Database.GetMigrations().Any())
                    throw new MissingMigrationsException();

                await db.Database.MigrateAsync(ct);
                await DbSeeder.SeedAsync(db, sp.GetRequiredService<IFileStorageService>(), sp.GetRequiredService<IClock>(), ct);
                await DbSeeder.SeedUsersAsync(db, sp.GetRequiredService<IPasswordHasher<AppUser>>(),
                    sp.GetRequiredService<IOptions<SeedUserOptions>>().Value, sp.GetRequiredService<IClock>(), ct);

                logger.LogInformation("Database ready (migrations applied, seed data checked).");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not MissingMigrationsException && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Database not ready yet (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}

/// <summary>A build problem, not a timing problem, so it is not retried.</summary>
public sealed class MissingMigrationsException : InvalidOperationException
{
    public MissingMigrationsException() : base("No EF Core migrations were found in GLMS.Api/Migrations.") { }
}
