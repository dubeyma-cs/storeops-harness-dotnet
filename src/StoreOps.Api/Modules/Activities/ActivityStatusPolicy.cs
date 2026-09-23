namespace StoreOps.Api.Modules.Activities;

/// <summary>
/// The activity lifecycle rules, in one place, as data.
/// </summary>
/// <remarks>
/// Extracted from <c>ActivityService</c> deliberately. The bulk-status handover path and the
/// single-activity PATCH path must agree on what a legal transition is; two copies of the rule
/// would eventually disagree, and the Evaluator's business-rule checks would pass against one
/// while the other drifted.
/// <para>DONE is terminal: a completed activity is a historical record, not a work item.</para>
/// </remarks>
public static class ActivityStatusPolicy
{
    private static readonly Dictionary<ActivityStatus, ActivityStatus[]> Allowed = new()
    {
        [ActivityStatus.TODO] = new[] { ActivityStatus.IN_PROGRESS, ActivityStatus.BLOCKED, ActivityStatus.DONE },
        [ActivityStatus.IN_PROGRESS] = new[] { ActivityStatus.BLOCKED, ActivityStatus.DONE, ActivityStatus.TODO },
        [ActivityStatus.BLOCKED] = new[] { ActivityStatus.TODO, ActivityStatus.IN_PROGRESS, ActivityStatus.DONE },
        [ActivityStatus.DONE] = Array.Empty<ActivityStatus>(),
    };

    /// <summary>Statuses an outgoing shift may set through the bulk handover endpoint.</summary>
    public static readonly ActivityStatus[] HandoverStatuses = { ActivityStatus.DONE, ActivityStatus.BLOCKED };

    /// <summary>True when <paramref name="to"/> is a legal next status from <paramref name="from"/>.</summary>
    public static bool CanTransition(ActivityStatus from, ActivityStatus to) =>
        from != to && Allowed[from].Contains(to);

    /// <summary>True when the status may be set via the bulk handover endpoint.</summary>
    public static bool IsHandoverStatus(ActivityStatus status) => HandoverStatuses.Contains(status);

    /// <summary>
    /// BLOCKED always requires a reason: an unexplained blocked activity is unactionable for the
    /// incoming shift, which is the whole point of the handover audit trail.
    /// </summary>
    public static bool RequiresReason(ActivityStatus to) => to == ActivityStatus.BLOCKED;
}
