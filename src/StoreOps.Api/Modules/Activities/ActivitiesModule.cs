namespace StoreOps.Api.Modules.Activities;

/// <summary>Composition root for the activities module.</summary>
public static class ActivitiesModule
{
    /// <summary>Registers the activities module's service, repository, options and worker.</summary>
    public static IServiceCollection AddActivitiesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ActivityOptions>(configuration.GetSection(ActivityOptions.SectionName));
        services.AddSingleton<IActivityRepository, InMemoryActivityRepository>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddHostedService<SlaSweepWorker>();
        return services;
    }
}
