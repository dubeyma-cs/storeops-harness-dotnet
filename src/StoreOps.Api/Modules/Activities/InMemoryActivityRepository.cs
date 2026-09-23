using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// In-memory activity store. Registered as a singleton so state survives across requests.
/// </summary>
/// <remarks>
/// Repositories in StoreOps hold no business logic: they filter, persist, and return. Notice
/// there is no <c>IStaffService</c>, <c>IEventBus</c>, or <c>HttpContext</c> anywhere in this
/// file — a repository that reached outward would break the layer separation rule.
/// </remarks>
public sealed class InMemoryActivityRepository : IActivityRepository
{
    private readonly ConcurrentDictionary<string, Activity> _activities = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<ActivityAuditEntry> _audit = new();

    public Task<IReadOnlyList<Activity>> ListAsync(
        ActivityListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _activities.Values
            .Where(a => string.Equals(a.StoreId, filter.StoreId, StringComparison.Ordinal));

        if (filter.ProgrammeId is { Length: > 0 })
        {
            query = query.Where(a => string.Equals(a.ProgrammeId, filter.ProgrammeId, StringComparison.Ordinal));
        }

        if (filter.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        IReadOnlyList<Activity> result = query
            .OrderByDescending(a => a.Priority)
            .ThenBy(a => a.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Activity?> FindAsync(
        string activityId,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        if (!_activities.TryGetValue(activityId, out var activity))
        {
            return Task.FromResult<Activity?>(null);
        }

        return Task.FromResult(
            string.Equals(activity.StoreId, storeId, StringComparison.Ordinal) ? activity : null);
    }

    public Task<IReadOnlyDictionary<string, Activity>> FindManyAsync(
        IEnumerable<string> activityIds,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activityIds);

        var found = new Dictionary<string, Activity>(StringComparer.Ordinal);

        foreach (var id in activityIds.Distinct(StringComparer.Ordinal))
        {
            if (_activities.TryGetValue(id, out var activity)
                && string.Equals(activity.StoreId, storeId, StringComparison.Ordinal))
            {
                found[id] = activity;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, Activity>>(found);
    }

    public Task AddAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activities[activity.Id] = activity;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _activities[activity.Id] = activity;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(
        string activityId,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        if (!_activities.TryGetValue(activityId, out var activity)
            || !string.Equals(activity.StoreId, storeId, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_activities.TryRemove(activityId, out _));
    }

    public Task AppendAuditAsync(
        IEnumerable<ActivityAuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            _audit.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityAuditEntry>> ListAuditAsync(
        string activityId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ActivityAuditEntry> result = _audit
            .Where(e => string.Equals(e.ActivityId, activityId, StringComparison.Ordinal))
            .OrderBy(e => e.RecordedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Activity>> ListUnflaggedOverdueAsync(
        DateTimeOffset instant,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Activity> result = _activities.Values
            .Where(a => a.IsSlaTracked && a.SlaBreachRaisedAt is null && a.IsOverdueAt(instant))
            .OrderBy(a => a.DueAt)
            .ToList();

        return Task.FromResult(result);
    }
}
