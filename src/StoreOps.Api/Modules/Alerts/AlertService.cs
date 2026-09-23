using Microsoft.Extensions.Options;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Shared.Time;

namespace StoreOps.Api.Modules.Alerts;

/// <inheritdoc cref="IAlertService"/>
/// <remarks>
/// Cross-module dependencies here are both read-only service-layer lookups:
/// <see cref="IStaffService"/> to resolve recipients and <see cref="IActivityService"/> to ask
/// "is this activity still open?" before escalating. Neither module's repository is touched.
/// </remarks>
public sealed class AlertService : IAlertService
{
    private const string DoneStatus = "DONE";

    private readonly IAlertRepository _repository;
    private readonly IStaffService _staffService;
    private readonly IActivityService _activityService;
    private readonly IStaffContextAccessor _staffContext;
    private readonly IClock _clock;
    private readonly AlertOptions _options;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IAlertRepository repository,
        IStaffService staffService,
        IActivityService activityService,
        IStaffContextAccessor staffContext,
        IClock clock,
        IOptions<AlertOptions> options,
        ILogger<AlertService> logger)
    {
        _repository = repository;
        _staffService = staffService;
        _activityService = activityService;
        _staffContext = staffContext;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    private StaffContext Caller =>
        _staffContext.Current
        ?? throw new UnauthorizedError("No authenticated staff identity on this request.");

    public Task<IReadOnlyList<Notification>> ListForCurrentStaffAsync(
        CancellationToken cancellationToken = default)
    {
        var caller = Caller;
        return _repository.ListForRecipientAsync(caller.StoreId, caller.StaffId, cancellationToken);
    }

    public async Task HandleSlaBreachedAsync(
        ActivitySlaBreachedEvent breach,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(breach);

        if (await _repository.ExistsForActivityAsync(breach.ActivityId, AlertType.SLA_BREACH, cancellationToken))
        {
            // The bus may redeliver; one breach must not produce two alerts.
            return;
        }

        var recipient = breach.DepartmentLeadStaffId ?? breach.AssigneeStaffId;

        if (recipient is null)
        {
            _logger.LogWarning(
                "SLA breach on {ActivityId} has no department lead or assignee to notify",
                breach.ActivityId);
            return;
        }

        var now = _clock.UtcNow;

        await _repository.AddAsync(
            new Notification
            {
                Id = NewId(),
                StoreId = breach.StoreId,
                RecipientStaffId = recipient,
                Type = AlertType.SLA_BREACH,
                Channel = NotificationChannel.IN_APP,
                Status = NotificationStatus.SENT,
                Title = $"SLA breach on activity {breach.ActivityId}",
                Body =
                    $"{breach.Priority} activity {breach.ActivityId} passed its due date "
                    + $"({breach.DueAt:u}) without reaching DONE.",
                RelatedActivityId = breach.ActivityId,
                SourceEventId = breach.EventId,
                CreatedAt = now,
            },
            cancellationToken);

        await _repository.UpsertEscalationAsync(
            new PendingEscalation(
                breach.ActivityId,
                breach.StoreId,
                breach.DepartmentLeadStaffId,
                now,
                now.Add(_options.EscalationGracePeriod)),
            cancellationToken);
    }

    public async Task HandleStatusChangedAsync(
        ActivityStatusChangedEvent statusChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statusChanged);

        if (string.Equals(statusChanged.ToStatus, DoneStatus, StringComparison.Ordinal))
        {
            await _repository.RemoveEscalationAsync(statusChanged.ActivityId, cancellationToken);
        }
    }

    public async Task HandleBulkStatusAppliedAsync(
        ActivitiesBulkStatusAppliedEvent applied,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applied);

        if (applied.UpdatedCount == 0)
        {
            return;
        }

        var managers = await _staffService.ListForStoreAsync(applied.StoreId, cancellationToken);
        var now = _clock.UtcNow;

        foreach (var manager in managers.Where(s => s.Role == StaffRole.STORE_MANAGER))
        {
            await _repository.AddAsync(
                new Notification
                {
                    Id = NewId(),
                    StoreId = applied.StoreId,
                    RecipientStaffId = manager.Id,
                    Type = AlertType.SHIFT_HANDOVER,
                    Channel = NotificationChannel.IN_APP,
                    Status = NotificationStatus.SENT,
                    Title = "Shift handover recorded",
                    Body =
                        $"{applied.UpdatedCount} of {applied.RequestedCount} activities were updated by "
                        + $"{applied.ActorStaffId} during handover ({applied.FailedCount} failed).",
                    SourceEventId = applied.EventId,
                    CreatedAt = now,
                },
                cancellationToken);
        }
    }

    public async Task<int> SweepEscalationsAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var due = await _repository.ListEscalationsDueAsync(now, cancellationToken);
        var escalated = 0;

        foreach (var pending in due)
        {
            // Read-only cross-module lookup: has the breach been resolved in the meantime?
            var activity = await _activityService.FindForStoreAsync(
                pending.ActivityId, pending.StoreId, cancellationToken);

            if (activity is null || string.Equals(activity.Status, DoneStatus, StringComparison.Ordinal))
            {
                await _repository.RemoveEscalationAsync(pending.ActivityId, cancellationToken);
                continue;
            }

            if (await _repository.ExistsForActivityAsync(pending.ActivityId, AlertType.ESCALATION, cancellationToken))
            {
                await _repository.RemoveEscalationAsync(pending.ActivityId, cancellationToken);
                continue;
            }

            var roster = await _staffService.ListForStoreAsync(pending.StoreId, cancellationToken);
            var managers = roster.Where(s => s.Role == StaffRole.STORE_MANAGER).ToList();

            if (managers.Count == 0)
            {
                _logger.LogWarning(
                    "Escalation for {ActivityId} has no STORE_MANAGER recipient in {StoreId}",
                    pending.ActivityId,
                    pending.StoreId);
                await _repository.RemoveEscalationAsync(pending.ActivityId, cancellationToken);
                continue;
            }

            foreach (var manager in managers)
            {
                await _repository.AddAsync(
                    new Notification
                    {
                        Id = NewId(),
                        StoreId = pending.StoreId,
                        RecipientStaffId = manager.Id,
                        Type = AlertType.ESCALATION,
                        Channel = NotificationChannel.IN_APP,
                        Status = NotificationStatus.SENT,
                        Title = $"Escalated SLA breach on activity {pending.ActivityId}",
                        Body =
                            $"Activity {pending.ActivityId} has been in breach since {pending.BreachedAt:u} and was "
                            + $"not resolved within the {_options.EscalationGracePeriod} grace period.",
                        RelatedActivityId = pending.ActivityId,
                        SourceEventId = $"escalation-{pending.ActivityId}",
                        CreatedAt = now,
                    },
                    cancellationToken);
            }

            await _repository.RemoveEscalationAsync(pending.ActivityId, cancellationToken);
            escalated++;
        }

        return escalated;
    }

    private static string NewId() => $"alr-{Guid.NewGuid():N}"[..16];
}
