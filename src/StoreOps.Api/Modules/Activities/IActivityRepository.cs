namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// Data access for the activities module.
/// </summary>
/// <remarks>
/// StoreOps architecture rule "module boundary": only <c>ActivityService</c> may depend on this
/// interface. No controller, no other module, and no repository in another module may reference
/// it. <c>StoreOps.ArchCheck</c> fails the build if that happens.
/// </remarks>
public interface IActivityRepository
{
    Task<IReadOnlyList<Activity>> ListAsync(
        ActivityListFilter filter,
        CancellationToken cancellationToken = default);

    Task<Activity?> FindAsync(string activityId, string storeId, CancellationToken cancellationToken = default);

    /// <summary>Loads several activities in one pass, keyed by id, for the bulk handover path.</summary>
    Task<IReadOnlyDictionary<string, Activity>> FindManyAsync(
        IEnumerable<string> activityIds,
        string storeId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Activity activity, CancellationToken cancellationToken = default);

    Task UpdateAsync(Activity activity, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string activityId, string storeId, CancellationToken cancellationToken = default);

    /// <summary>Appends audit rows. Append-only: there is no update or delete counterpart.</summary>
    Task AppendAuditAsync(
        IEnumerable<ActivityAuditEntry> entries,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityAuditEntry>> ListAuditAsync(
        string activityId,
        CancellationToken cancellationToken = default);

    /// <summary>SLA-tracked activities past their due date that have not yet been flagged.</summary>
    Task<IReadOnlyList<Activity>> ListUnflaggedOverdueAsync(
        DateTimeOffset instant,
        CancellationToken cancellationToken = default);
}
