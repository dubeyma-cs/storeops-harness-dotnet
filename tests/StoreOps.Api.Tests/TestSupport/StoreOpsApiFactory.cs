using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StoreOps.Api.Shared.Time;

namespace StoreOps.Api.Tests.TestSupport;

/// <summary>
/// Boots the real StoreOps pipeline in memory for integration tests.
/// </summary>
/// <remarks>
/// Two deliberate substitutions, and nothing else: the clock becomes a <see cref="FixedClock"/>,
/// and the background sweeps are switched off. Everything else — middleware order, the event
/// bus, module registration, the in-memory repositories — is exactly what runs in production, so
/// a passing integration test says something about the deployed application.
/// </remarks>
public sealed class StoreOpsApiFactory : WebApplicationFactory<IApiMarker>
{
    public FixedClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);

        builder.UseSetting("Activities:EnableBackgroundSlaSweep", "false");
        builder.UseSetting("Alerts:EnableBackgroundEscalationSweep", "false");
        builder.UseSetting("Alerts:EscalationGracePeriod", "04:00:00");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }
}
