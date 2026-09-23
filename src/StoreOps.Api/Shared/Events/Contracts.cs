namespace StoreOps.Api.Shared.Events;

/*
 * Event contracts live in Shared, not in the publishing module.
 *
 * If ActivityStatusChangedEvent lived in Modules/Activities, every subscriber would have to
 * import the activities module to read it — reintroducing exactly the coupling the event bus
 * exists to remove. For the same reason payloads carry enum values as strings: the alerts
 * module reacts to "CRITICAL" without importing ActivityPriority.
 */

/// <summary>Raised when a new operational activity is created.</summary>
public sealed record ActivityCreatedEvent(
    string ActivityId,
    string StoreId,
    string? ProgrammeId,
    string Priority,
    string Category,
    string? AssigneeId,
    string CreatedByStaffId) : DomainEvent
{
    public override string Name => "activity.created";

    public override string SourceModule => "activities";
}

/// <summary>Raised on every accepted activity status transition.</summary>
public sealed record ActivityStatusChangedEvent(
    string ActivityId,
    string StoreId,
    string FromStatus,
    string ToStatus,
    string Priority,
    string Category,
    string? AssigneeId,
    string ChangedByStaffId,
    string Reason) : DomainEvent
{
    public override string Name => "activity.status-changed";

    public override string SourceModule => "activities";
}

/// <summary>
/// Raised when a HIGH or CRITICAL activity passes its due date without reaching DONE.
/// The alerts module turns this into an SLA_BREACH notification; activities does not know that.
/// </summary>
public sealed record ActivitySlaBreachedEvent(
    string ActivityId,
    string StoreId,
    string Priority,
    string? AssigneeStaffId,
    string? DepartmentLeadStaffId,
    DateTimeOffset DueAt) : DomainEvent
{
    public override string Name => "activity.sla-breached";

    public override string SourceModule => "activities";
}

/// <summary>
/// Raised once per shift-handover bulk status request, after partial-failure resolution.
/// Carries the aggregate outcome so reports can track handover throughput without reading
/// the activities repository.
/// </summary>
public sealed record ActivitiesBulkStatusAppliedEvent(
    string StoreId,
    string ActorStaffId,
    int RequestedCount,
    int UpdatedCount,
    int FailedCount,
    IReadOnlyList<string> UpdatedActivityIds) : DomainEvent
{
    public override string Name => "activities.bulk-status-applied";

    public override string SourceModule => "activities";
}

/// <summary>Raised when a staff member is added to a store programme.</summary>
public sealed record ProgrammeMemberAddedEvent(
    string ProgrammeId,
    string StoreId,
    string StaffId,
    string Role,
    string AddedByStaffId) : DomainEvent
{
    public override string Name => "programme.member-added";

    public override string SourceModule => "programmes";
}

/// <summary>
/// Raised when a store programme is closed. The reports module reacts by queueing a
/// STORE_SUMMARY report; programmes has no dependency on the reports module.
/// </summary>
public sealed record ProgrammeClosedEvent(
    string ProgrammeId,
    string StoreId,
    string RegionId,
    string ClosedByStaffId) : DomainEvent
{
    public override string Name => "programme.closed";

    public override string SourceModule => "programmes";
}
