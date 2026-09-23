namespace StoreOps.Api.Modules.Programmes;

/// <summary>Composition root for the programmes module.</summary>
public static class ProgrammesModule
{
    /// <summary>Registers the programmes module's service and repository.</summary>
    public static IServiceCollection AddProgrammesModule(this IServiceCollection services)
    {
        services.AddSingleton<IProgrammeRepository, InMemoryProgrammeRepository>();
        services.AddScoped<IProgrammeService, ProgrammeService>();
        return services;
    }
}
