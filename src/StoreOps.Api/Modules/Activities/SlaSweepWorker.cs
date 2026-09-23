using Microsoft.Extensions.Options;
using StoreOps.Api.Shared.Auth;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// Periodically asks the activities service to raise SLA breach events.
/// </summary>
/// <remarks>
/// The worker holds no business rule of its own — the "HIGH or CRITICAL, past due, not DONE"
/// condition lives in the service and repository so it can be unit tested with a fake clock.
/// Disabled by configuration in tests and CI (<c>Activities:EnableBackgroundSlaSweep=false</c>)
/// so the harness Evaluator never sees a flaky test caused by a background tick.
/// </remarks>
public sealed class SlaSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityOptions _options;
    private readonly ILogger<SlaSweepWorker> _logger;

    public SlaSweepWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ActivityOptions> options,
        ILogger<SlaSweepWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundSlaSweep)
        {
            _logger.LogInformation("Background SLA sweep disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(_options.SlaSweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = _scopeFactory.CreateScope();

                // The sweep runs outside any HTTP request, so it supplies its own system identity.
                scope.ServiceProvider
                    .GetRequiredService<IStaffContextAccessor>()
                    .Set(SystemIdentity.SlaSweep);

                var service = scope.ServiceProvider.GetRequiredService<IActivityService>();
                await service.SweepSlaBreachesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep must not take the host down; the next tick retries.
                _logger.LogError(ex, "SLA sweep tick failed");
            }
        }
    }
}
