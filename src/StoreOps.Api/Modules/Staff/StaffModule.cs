namespace StoreOps.Api.Modules.Staff;

/// <summary>Composition root for the staff module.</summary>
public static class StaffModule
{
    /// <summary>Registers the staff module's service and repository.</summary>
    public static IServiceCollection AddStaffModule(this IServiceCollection services)
    {
        services.AddSingleton<IStaffRepository, InMemoryStaffRepository>();
        services.AddScoped<IStaffService, StaffService>();
        return services;
    }
}
