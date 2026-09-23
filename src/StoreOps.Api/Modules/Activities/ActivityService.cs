using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Shared.Time;

namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// Business logic for the activities module.
/// </summary>
/// <remarks>
/// Dependencies worth noticing, because they encode the StoreOps boundary rules:
/// <list type="bullet">
///   <item><see cref="IActivityRepository"/> — this module's own repository, nobody else's.</item>
///   <item><see cref="IStaffService"/> — another module's <em>service</em>, used only for
///   read-only lookups (does this assignee exist, who leads this department).</item>
///   <item><see cref="IEventBus"/> — every cross-module side effect. There is no
///   <c>IAlertService</c> or <c>IReportService</c> here, and there never may be.</item>
/// </list>
/// </remarks>
public sealed class ActivityService : IActivityService
{
    private readonly IActivityRepository _repository;
    private readonly IStaffService _staffService;
    private readonly IEventBus _eventBus;
    private readonly IStaffContextAccessor _staffContext;
    private readonly IClock _clock;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(
        IActivityRepository repository,
        IStaffService staffService,
        IEventBus eventBus,
        IStaffContextAccessor staffContext,
        IClock clock,
        ILogger<ActivityService> logger)
    {
        _repository = repository;
        _staffService = staffService;
        _eventBus = eventBus;
        _staffContext = staffContext;
        _clock = clock;
        _logger = logger;
    }

    private StaffContext Caller =>
        _staffContext.Current
        ?? throw new UnauthorizedError("No authenticated staff identity on this request.");

    // -----------------------------------------------------------------------------------
    // Reads
    // -----------------------------------------------------------------------------------

    public Task<IReadOnlyList<Activity>> ListAsync(
        string? programmeId,
        ActivityStatus? status,
        CancellationToken cancellationToken = default) =>
        _repository.ListAsync(
            new ActivityListFilter(Caller.StoreId, programmeId, status),
            cancellationToken);

    public async Task<Activity> GetAsync(string activityId, CancellationToken cancellationToken = default)
    {
        var activity = await _repository.FindAsync(activityId, Caller.StoreId, cancellationToken);

        if (activity is null)
        {
            throw new NotFoundError("Activity", activityId);
        }

        return activity;
    }

    public async Task<IReadOnlyList<ActivityAuditEntry>> ListAuditAsync(
        string activityId,
        CancellationToken cancellationToken = default)
    {
        // Confirms store scope before exposing the audit trail.
        _ = await GetAsync(activityId, cancellationToken);
        return await _repository.ListAuditAsync(activityId, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityReadModel>> ListForStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var activities = await _repository.ListAsync(new ActivityListFilter(storeId), cancellationToken);
        var now = _clock.UtcNow;

        return activities.Select(a => ToReadModel(a, now)).ToList();
    }

    public async Task<ActivityReadModel?> FindForStoreAsync(
        string activityId,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var activity = await _repository.FindAsync(activityId, storeId, cancellationToken);
        return activity is null ? null : ToReadModel(activity, _clock.UtcNow);
    }

    private static ActivityReadModel ToReadModel(Activity activity, DateTimeOffset now) => new(
        activity.Id,
        activity.StoreId,
        activity.ProgrammeId,
        activity.Status.ToString(),
        activity.Priority.ToString(),
        activity.Category.ToString(),
        activity.Department,
        activity.AssigneeStaffId,
        activity.DueAt,
        activity.CompletedAt,
        activity.IsOverdueAt(now));

    // -----------------------------------------------------------------------------------
    // Writes
    // -----------------------------------------------------------------------------------

    public async Task<Activity> CreateAsync(
        CreateActivityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = Caller;
        var now = _clock.UtcNow;

        if (request.DueAt is { } dueAt && dueAt <= now)
        {
            throw ValidationError.ForField(nameof(request.DueAt), "Due date must be in the future.");
        }

        await EnsureAssigneeIsInStoreAsync(request.AssigneeStaffId, caller.StoreId, cancellationToken);

        var activity = new Activity
        {
            Id = $"act-{Guid.NewGuid():N}"[..12],
            StoreId = caller.StoreId,
            ProgrammeId = string.IsNullOrWhiteSpace(request.ProgrammeId) ? null : request.ProgrammeId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Status = ActivityStatus.TODO,
            Priority = request.Priority,
            Category = request.Category,
            Department = request.Department.Trim().ToUpperInvariant(),
            AssigneeStaffId = request.AssigneeStaffId,
            CreatedByStaffId = caller.StaffId,
            DueAt = request.DueAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _repository.AddAsync(activity, cancellationToken);

        await _repository.AppendAuditAsync(
            new[] { BuildAudit(activity, caller.StaffId, "CREATED", null, activity.Status, null, "api.single", now) },
            cancellationToken);

        await _eventBus.PublishAsync(
            new ActivityCreatedEvent(
                activity.Id,
                activity.StoreId,
                activity.ProgrammeId,
                activity.Priority.ToString(),
                activity.Category.ToString(),
                activity.AssigneeStaffId,
                caller.StaffId),
            cancellationToken);

        return activity;
    }

    public async Task<Activity> UpdateAsync(
        string activityId,
        UpdateActivityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = Caller;
        var existing = await GetAsync(activityId, cancellationToken);
        var now = _clock.UtcNow;

        if (request.Status is null
            && request.Priority is null
            && request.Category is null
            && request.AssigneeStaffId is null)
        {
            throw new ValidationError("At least one of status, priority, category or assigneeStaffId is required.");
        }

        await EnsureAssigneeIsInStoreAsync(request.AssigneeStaffId, caller.StoreId, cancellationToken);

        var updated = existing with
        {
            Priority = request.Priority ?? existing.Priority,
            Category = request.Category ?? existing.Category,
            AssigneeStaffId = request.AssigneeStaffId ?? existing.AssigneeStaffId,
            UpdatedAt = now,
        };

        var audit = new List<ActivityAuditEntry>();

        if (request.Status is { } target && target != existing.Status)
        {
            ApplyStatusGuards(existing, target, request.Reason);

            updated = updated with
            {
                Status = target,
                CompletedAt = target == ActivityStatus.DONE ? now : null,
            };

            audit.Add(BuildAudit(
                updated, caller.StaffId, "STATUS_CHANGED", existing.Status, target, request.Reason, "api.single", now));
        }
        else
        {
            audit.Add(BuildAudit(
                updated, caller.StaffId, "FIELDS_UPDATED", null, null, request.Reason, "api.single", now));
        }

        await _repository.UpdateAsync(updated, cancellationToken);
        await _repository.AppendAuditAsync(audit, cancellationToken);

        if (updated.Status != existing.Status)
        {
            await PublishStatusChangedAsync(
                existing, updated, caller.StaffId, request.Reason ?? "single update", cancellationToken);
        }

        return updated;
    }

    public async Task DeleteAsync(string activityId, CancellationToken cancellationToken = default)
    {
        var caller = Caller;
        var activity = await GetAsync(activityId, cancellationToken);

        var isOwner = string.Equals(activity.CreatedByStaffId, caller.StaffId, StringComparison.Ordinal);

        if (!isOwner && !caller.IsStoreManagement)
        {
            throw new ForbiddenError(
                "Only the staff member who created the activity, or a store manager, may delete it.");
        }

        var deleted = await _repository.DeleteAsync(activityId, caller.StoreId, cancellationToken);

        if (!deleted)
        {
            throw new NotFoundError("Activity", activityId);
        }

        await _repository.AppendAuditAsync(
            new[]
            {
                BuildAudit(
                    activity, caller.StaffId, "DELETED", activity.Status, null, null, "api.single", _clock.UtcNow),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Partial-failure contract: each item is validated and applied independently against the
    /// state loaded for this request. One bad id or one illegal transition never rolls back the
    /// items that succeeded — an outgoing shift should not lose 40 good updates because the 41st
    /// activity was already closed by someone else.
    /// </remarks>
    public async Task<BulkStatusUpdateResponse> BulkUpdateStatusAsync(
        BulkStatusUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = Caller;
        var now = _clock.UtcNow;

        if (request.Items.Count == 0)
        {
            throw new ValidationError("At least one item is required.");
        }

        if (request.Items.Count > BulkStatusUpdateRequest.MaxItems)
        {
            throw ValidationError.ForField(
                nameof(request.Items),
                $"A handover request may contain at most {BulkStatusUpdateRequest.MaxItems} activities.");
        }

        var duplicateIds = request.Items
            .GroupBy(i => i.ActivityId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            throw ValidationError.ForField(
                nameof(request.Items),
                $"Duplicate activity ids are not allowed: {string.Join(", ", duplicateIds)}.");
        }

        var loaded = await _repository.FindManyAsync(
            request.Items.Select(i => i.ActivityId),
            caller.StoreId,
            cancellationToken);

        var results = new List<BulkStatusItemResult>(request.Items.Count);
        var audit = new List<ActivityAuditEntry>();
        var statusChanges = new List<(Activity Before, Activity After, string Reason)>();

        foreach (var item in request.Items)
        {
            if (!loaded.TryGetValue(item.ActivityId, out var existing))
            {
                results.Add(BulkStatusItemResult.Failed(
                    item.ActivityId,
                    ErrorCodes.NotFound,
                    $"Activity '{item.ActivityId}' was not found in store '{caller.StoreId}'."));
                continue;
            }

            var failure = ValidateHandoverItem(existing, item);

            if (failure is not null)
            {
                results.Add(failure);
                continue;
            }

            var updated = existing with
            {
                Status = item.Status,
                CompletedAt = item.Status == ActivityStatus.DONE ? now : null,
                UpdatedAt = now,
            };

            await _repository.UpdateAsync(updated, cancellationToken);

            audit.Add(BuildAudit(
                updated,
                caller.StaffId,
                "STATUS_CHANGED",
                existing.Status,
                item.Status,
                item.Reason,
                "api.bulk-status",
                now));

            statusChanges.Add((existing, updated, item.Reason ?? "shift handover"));
            results.Add(BulkStatusItemResult.Updated(item.ActivityId, item.Status));
        }

        // One audit row per updated activity, written even when some items failed.
        if (audit.Count > 0)
        {
            await _repository.AppendAuditAsync(audit, cancellationToken);
        }

        foreach (var (before, after, reason) in statusChanges)
        {
            await PublishStatusChangedAsync(before, after, caller.StaffId, reason, cancellationToken);
        }

        var updatedCount = results.Count(r => r.Outcome == BulkStatusItemResult.OutcomeUpdated);
        var failedCount = results.Count - updatedCount;

        await _eventBus.PublishAsync(
            new ActivitiesBulkStatusAppliedEvent(
                caller.StoreId,
                caller.StaffId,
                request.Items.Count,
                updatedCount,
                failedCount,
                statusChanges.Select(c => c.After.Id).ToList()),
            cancellationToken);

        _logger.LogInformation(
            "Shift handover by {StaffId} on {StoreId}: {Updated} updated, {Failed} failed of {Requested}",
            caller.StaffId,
            caller.StoreId,
            updatedCount,
            failedCount,
            request.Items.Count);

        return new BulkStatusUpdateResponse(request.Items.Count, updatedCount, failedCount, results);
    }

    public async Task<int> SweepSlaBreachesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var breached = await _repository.ListUnflaggedOverdueAsync(now, cancellationToken);

        foreach (var activity in breached)
        {
            var lead = await _staffService.FindDepartmentLeadAsync(
                activity.StoreId, activity.Department, cancellationToken);

            await _repository.UpdateAsync(activity with { SlaBreachRaisedAt = now }, cancellationToken);

            await _eventBus.PublishAsync(
                new ActivitySlaBreachedEvent(
                    activity.Id,
                    activity.StoreId,
                    activity.Priority.ToString(),
                    activity.AssigneeStaffId,
                    lead?.Id,
                    activity.DueAt ?? now),
                cancellationToken);
        }

        if (breached.Count > 0)
        {
            _logger.LogInformation("SLA sweep raised {Count} breach event(s)", breached.Count);
        }

        return breached.Count;
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static void ApplyStatusGuards(Activity existing, ActivityStatus target, string? reason)
    {
        if (!ActivityStatusPolicy.CanTransition(existing.Status, target))
        {
            throw new InvalidStateTransitionError(existing.Status.ToString(), target.ToString());
        }

        if (ActivityStatusPolicy.RequiresReason(target) && string.IsNullOrWhiteSpace(reason))
        {
            throw ValidationError.ForField("reason", "A reason is required when setting status to BLOCKED.");
        }
    }

    private static BulkStatusItemResult? ValidateHandoverItem(Activity existing, BulkStatusItem item)
    {
        if (!ActivityStatusPolicy.IsHandoverStatus(item.Status))
        {
            return BulkStatusItemResult.Failed(
                item.ActivityId,
                ErrorCodes.Validation,
                $"Shift handover may only set {string.Join(" or ", ActivityStatusPolicy.HandoverStatuses)}; got '{item.Status}'.");
        }

        if (!ActivityStatusPolicy.CanTransition(existing.Status, item.Status))
        {
            return BulkStatusItemResult.Failed(
                item.ActivityId,
                ErrorCodes.InvalidStateTransition,
                $"Transition from '{existing.Status}' to '{item.Status}' is not permitted.");
        }

        if (ActivityStatusPolicy.RequiresReason(item.Status) && string.IsNullOrWhiteSpace(item.Reason))
        {
            return BulkStatusItemResult.Failed(
                item.ActivityId,
                ErrorCodes.Validation,
                "A reason is required when setting status to BLOCKED.");
        }

        return null;
    }

    private async Task EnsureAssigneeIsInStoreAsync(
        string? assigneeStaffId,
        string storeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assigneeStaffId))
        {
            return;
        }

        // Cross-module read-only lookup through the staff module's service layer.
        var assignee = await _staffService.FindAsync(assigneeStaffId, cancellationToken);

        if (assignee is null || !string.Equals(assignee.StoreId, storeId, StringComparison.Ordinal))
        {
            throw ValidationError.ForField(
                "assigneeStaffId",
                $"Staff member '{assigneeStaffId}' is not active in store '{storeId}'.");
        }
    }

    private Task PublishStatusChangedAsync(
        Activity before,
        Activity after,
        string actorStaffId,
        string reason,
        CancellationToken cancellationToken) =>
        _eventBus.PublishAsync(
            new ActivityStatusChangedEvent(
                after.Id,
                after.StoreId,
                before.Status.ToString(),
                after.Status.ToString(),
                after.Priority.ToString(),
                after.Category.ToString(),
                after.AssigneeStaffId,
                actorStaffId,
                reason),
            cancellationToken);

    private static ActivityAuditEntry BuildAudit(
        Activity activity,
        string actorStaffId,
        string action,
        ActivityStatus? from,
        ActivityStatus? to,
        string? reason,
        string source,
        DateTimeOffset now) =>
        new(
            Id: $"aud-{Guid.NewGuid():N}"[..16],
            ActivityId: activity.Id,
            StoreId: activity.StoreId,
            ActorStaffId: actorStaffId,
            Action: action,
            FromStatus: from?.ToString(),
            ToStatus: to?.ToString(),
            Reason: reason,
            Source: source,
            RecordedAt: now);
}
