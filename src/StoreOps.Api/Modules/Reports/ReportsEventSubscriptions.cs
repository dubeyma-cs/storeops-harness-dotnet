using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Reports;

/// <summary>Wires the reports module to the event bus.</summary>
/// <remarks>
/// The programmes module raises <c>ProgrammeClosedEvent</c> and knows nothing about reports;
/// this file is the entire link between them.
/// </remarks>
public sealed class ReportsEventSubscriptions : EventSubscriberService
{
    /// <summary>Identity report handlers run under.</summary>
    public static readonly StaffContext Identity = new(
        StaffId: "system-reports",
        StoreId: "*",
        RegionId: "*",
        Role: "SYSTEM");

    public ReportsEventSubscriptions(
        IEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        ILogger<ReportsEventSubscriptions> logger)
        : base(eventBus, scopeFactory, logger)
    {
    }

    protected override StaffContext HandlerIdentity => Identity;

    protected override void Configure()
    {
        On<ProgrammeClosedEvent>((services, closed, token) =>
            services.GetRequiredService<IReportService>().HandleProgrammeClosedAsync(closed, token));
    }
}
