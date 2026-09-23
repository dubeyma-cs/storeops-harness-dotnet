using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>
/// Wires the alerts module to the event bus.
/// </summary>
/// <remarks>
/// This file is the alerts module's entire inbound coupling to the rest of StoreOps. Nothing in
/// activities or programmes references the alerts module; they publish, alerts listens.
/// </remarks>
public sealed class AlertsEventSubscriptions : EventSubscriberService
{
    /// <summary>Identity alert handlers run under; store scope comes from the event payload.</summary>
    public static readonly StaffContext Identity = new(
        StaffId: "system-alerts",
        StoreId: "*",
        RegionId: "*",
        Role: "SYSTEM");

    public AlertsEventSubscriptions(
        IEventBus eventBus,
        IServiceScopeFactory scopeFactory,
        ILogger<AlertsEventSubscriptions> logger)
        : base(eventBus, scopeFactory, logger)
    {
    }

    protected override StaffContext HandlerIdentity => Identity;

    protected override void Configure()
    {
        On<ActivitySlaBreachedEvent>((services, breach, token) =>
            services.GetRequiredService<IAlertService>().HandleSlaBreachedAsync(breach, token));

        On<ActivityStatusChangedEvent>((services, statusChanged, token) =>
            services.GetRequiredService<IAlertService>().HandleStatusChangedAsync(statusChanged, token));

        On<ActivitiesBulkStatusAppliedEvent>((services, applied, token) =>
            services.GetRequiredService<IAlertService>().HandleBulkStatusAppliedAsync(applied, token));
    }
}
