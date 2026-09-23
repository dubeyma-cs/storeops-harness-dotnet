using Microsoft.Extensions.Options;
using StoreOps.Api.Shared.Auth;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>Periodically escalates SLA breaches whose grace period has lapsed.</summary>
public sealed class EscalationSweepWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertOptions _options;
    private readonly ILogger<EscalationSweepWorker> _logger;

    public EscalationSweepWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AlertOptions> options,
        ILogger<EscalationSweepWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundEscalationSweep)
        {
            _logger.LogInformation("Background escalation sweep disabled by configuration");
            return;
        }

        using var timer = new PeriodicTimer(_options.EscalationSweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider
                    .GetRequiredService<IStaffContextAccessor>()
                    .Set(AlertsEventSubscriptions.Identity);

                await scope.ServiceProvider
                    .GetRequiredService<IAlertService>()
                    .SweepEscalationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Escalation sweep tick failed");
            }
        }
    }
}
