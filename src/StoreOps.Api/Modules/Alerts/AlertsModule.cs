namespace StoreOps.Api.Modules.Alerts;

/// <summary>Composition root for the alerts module.</summary>
public static class AlertsModule
{
    /// <summary>Registers the alerts module's service, repository, subscriptions and worker.</summary>
    public static IServiceCollection AddAlertsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AlertOptions>(configuration.GetSection(AlertOptions.SectionName));
        services.AddSingleton<IAlertRepository, InMemoryAlertRepository>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddHostedService<AlertsEventSubscriptions>();
        services.AddHostedService<EscalationSweepWorker>();
        return services;
    }
}
