using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>
/// Business layer of the alerts module.
/// </summary>
/// <remarks>
/// Every member that creates an alert takes a <see cref="DomainEvent"/>, not a free-form
/// request. That is deliberate: alerts exist only as a reaction to something that happened
/// elsewhere in StoreOps, and the bus is the only way in. There is no
/// <c>CreateNotificationAsync(recipient, text)</c> for another module to call directly, so the
/// event-bus-only rule cannot be bypassed even by a well-meaning Generator.
/// </remarks>
public interface IAlertService
{
    /// <summary>Alerts addressed to the authenticated staff member.</summary>
    Task<IReadOnlyList<Notification>> ListForCurrentStaffAsync(CancellationToken cancellationToken = default);

    /// <summary>Raises an SLA_BREACH alert for the department lead and schedules escalation.</summary>
    Task HandleSlaBreachedAsync(ActivitySlaBreachedEvent breach, CancellationToken cancellationToken = default);

    /// <summary>Clears a pending escalation when the activity reaches DONE.</summary>
    Task HandleStatusChangedAsync(
        ActivityStatusChangedEvent statusChanged,
        CancellationToken cancellationToken = default);

    /// <summary>Notifies the incoming shift that a handover batch was applied.</summary>
    Task HandleBulkStatusAppliedAsync(
        ActivitiesBulkStatusAppliedEvent applied,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Escalates breaches whose grace period has lapsed and that are still not DONE.
    /// Returns the number of escalation alerts raised.
    /// </summary>
    Task<int> SweepEscalationsAsync(CancellationToken cancellationToken = default);
}
