using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Alerts;

/// <summary>In-memory alert store, registered as a singleton.</summary>
public sealed class InMemoryAlertRepository : IAlertRepository
{
    private readonly ConcurrentDictionary<string, Notification> _notifications = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingEscalation> _escalations = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        string storeId,
        string recipientStaffId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Notification> result = _notifications.Values
            .Where(n =>
                string.Equals(n.StoreId, storeId, StringComparison.Ordinal)
                && string.Equals(n.RecipientStaffId, recipientStaffId, StringComparison.Ordinal))
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _notifications[notification.Id] = notification;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsForActivityAsync(
        string activityId,
        AlertType type,
        CancellationToken cancellationToken = default)
    {
        var exists = _notifications.Values.Any(n =>
            n.Type == type && string.Equals(n.RelatedActivityId, activityId, StringComparison.Ordinal));

        return Task.FromResult(exists);
    }

    public Task UpsertEscalationAsync(PendingEscalation escalation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(escalation);
        _escalations[escalation.ActivityId] = escalation;
        return Task.CompletedTask;
    }

    public Task RemoveEscalationAsync(string activityId, CancellationToken cancellationToken = default)
    {
        _escalations.TryRemove(activityId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingEscalation>> ListEscalationsDueAsync(
        DateTimeOffset instant,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PendingEscalation> result = _escalations.Values
            .Where(e => instant >= e.EscalateAfter)
            .OrderBy(e => e.EscalateAfter)
            .ToList();

        return Task.FromResult(result);
    }
}
