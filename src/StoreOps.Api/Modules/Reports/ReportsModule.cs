namespace StoreOps.Api.Modules.Reports;

/// <summary>Composition root for the reports module.</summary>
/// <remarks>
/// No controller is registered: per the StoreOps base API surface, reports has no REST endpoints
/// in the baseline. It is reachable only through the event bus until a harness run adds the
/// regional rollup endpoint.
/// </remarks>
public static class ReportsModule
{
    /// <summary>Registers the reports module's service, repository and subscriptions.</summary>
    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        services.AddSingleton<IReportRepository, InMemoryReportRepository>();
        services.AddScoped<IReportService, ReportService>();
        services.AddHostedService<ReportsEventSubscriptions>();
        return services;
    }
}
