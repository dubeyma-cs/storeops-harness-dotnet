namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// The activities module's business layer and its only public face.
/// </summary>
/// <remarks>
/// Members are split into two groups. The write surface is reachable only from
/// <c>ActivitiesController</c>. The read surface (<see cref="ListForStoreAsync"/>) is what other
/// modules — currently reports — are allowed to call for cross-module lookups; it returns
/// <see cref="ActivityReadModel"/> projections, never the entity.
/// </remarks>
public interface IActivityService
{
    // --- Write surface: activities module's own HTTP routes only -------------------------

    Task<Activity> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken = default);

    Task<Activity> UpdateAsync(
        string activityId,
        UpdateActivityRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string activityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a shift-handover bulk status change. Individual item failures do not abort the
    /// request: every item is resolved and reported independently.
    /// </summary>
    Task<BulkStatusUpdateResponse> BulkUpdateStatusAsync(
        BulkStatusUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises <c>ActivitySlaBreachedEvent</c> for newly breached SLA-tracked activities.
    /// Returns the number of activities flagged.
    /// </summary>
    Task<int> SweepSlaBreachesAsync(CancellationToken cancellationToken = default);

    // --- Read surface ------------------------------------------------------------------

    Task<IReadOnlyList<Activity>> ListAsync(
        string? programmeId,
        ActivityStatus? status,
        CancellationToken cancellationToken = default);

    Task<Activity> GetAsync(string activityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityAuditEntry>> ListAuditAsync(
        string activityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-module read-only lookup. Intended for the reports module; returns projections so
    /// callers cannot mutate activity state.
    /// </summary>
    Task<IReadOnlyList<ActivityReadModel>> ListForStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-module read-only lookup of a single activity, scoped explicitly by store rather
    /// than by the ambient request identity. Used by the alerts module's escalation sweep, which
    /// runs outside any HTTP request.
    /// </summary>
    Task<ActivityReadModel?> FindForStoreAsync(
        string activityId,
        string storeId,
        CancellationToken cancellationToken = default);
}
