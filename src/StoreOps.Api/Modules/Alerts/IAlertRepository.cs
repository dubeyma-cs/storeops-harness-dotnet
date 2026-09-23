namespace StoreOps.Api.Modules.Alerts;

/// <summary>Data access for the alerts module. Only <c>AlertService</c> may depend on it.</summary>
public interface IAlertRepository
{
    Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        string storeId,
        string recipientStaffId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>True when an alert of this type already exists for the activity (idempotency).</summary>
    Task<bool> ExistsForActivityAsync(
        string activityId,
        AlertType type,
        CancellationToken cancellationToken = default);

    Task UpsertEscalationAsync(PendingEscalation escalation, CancellationToken cancellationToken = default);

    Task RemoveEscalationAsync(string activityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingEscalation>> ListEscalationsDueAsync(
        DateTimeOffset instant,
        CancellationToken cancellationToken = default);
}
