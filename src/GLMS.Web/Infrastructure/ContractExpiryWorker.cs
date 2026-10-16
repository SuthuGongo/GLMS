using GLMS.Core.Services;

namespace GLMS.Web.Infrastructure;

/// <summary>
/// "Automated status tracking": a background service that expires contracts whose end date has
/// passed. It runs shortly after start-up and then on a timer, fully async (no thread blocking).
/// A new DI scope is created per run because ContractService/DbContext are scoped services.
/// </summary>
public sealed class ContractExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContractExpiryWorker> _logger;
    private readonly TimeSpan _interval;

    public ContractExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<ContractExpiryWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(configuration.GetValue("ContractExpiry:IntervalMinutes", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so migrations/seeding finish first.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var contracts = scope.ServiceProvider.GetRequiredService<ContractService>();
                var expired = await contracts.ExpireOverdueContractsAsync(stoppingToken);
                if (expired > 0)
                    _logger.LogInformation("Contract expiry check: {Count} contract(s) automatically expired.", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let the worker crash the host; try again next tick.
                _logger.LogError(ex, "Contract expiry check failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
