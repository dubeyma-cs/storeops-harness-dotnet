namespace StoreOps.Api.Modules.Activities;

/*
 * Naming note (see .harness/skills/app-context/SKILL.md):
 * the specification's `Task` entity is modelled as `Activity` here. `Task` is an unavoidable
 * collision with System.Threading.Tasks.Task in an async C# codebase, and the module and its
 * REST surface are both named "activities" anyway. Enum member names are kept verbatim from
 * the specification so the wire contract is unchanged.
 */

/// <summary>Lifecycle state of an operational activity.</summary>
public enum ActivityStatus
{
    TODO = 0,
    IN_PROGRESS = 1,
    DONE = 2,
    BLOCKED = 3,
}

/// <summary>Operational urgency. HIGH and CRITICAL activities are SLA-tracked.</summary>
public enum ActivityPriority
{
    LOW = 0,
    MEDIUM = 1,
    HIGH = 2,
    CRITICAL = 3,
}

/// <summary>The kind of store work an activity represents.</summary>
public enum ActivityCategory
{
    RESTOCKING = 0,
    PLANOGRAM = 1,
    AUDIT = 2,
    COMPLIANCE = 3,
    GENERAL = 4,
}

/// <summary>An operational activity on a store's work list.</summary>
public sealed record Activity
{
    public required string Id { get; init; }

    public required string StoreId { get; init; }

    public string? ProgrammeId { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public ActivityStatus Status { get; init; } = ActivityStatus.TODO;

    public ActivityPriority Priority { get; init; } = ActivityPriority.MEDIUM;

    public ActivityCategory Category { get; init; } = ActivityCategory.GENERAL;

    public required string Department { get; init; }

    public string? AssigneeStaffId { get; init; }

    public required string CreatedByStaffId { get; init; }

    public DateTimeOffset? DueAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Set when the SLA breach event has already been raised, so it is raised once.</summary>
    public DateTimeOffset? SlaBreachRaisedAt { get; init; }

    /// <summary>Set when the breach has been escalated past the department lead.</summary>
    public DateTimeOffset? SlaEscalatedAt { get; init; }

    /// <summary>True when a HIGH/CRITICAL activity is past its due date and not DONE.</summary>
    public bool IsSlaTracked =>
        Priority is ActivityPriority.HIGH or ActivityPriority.CRITICAL;

    public bool IsOverdueAt(DateTimeOffset instant) =>
        DueAt is { } due && Status != ActivityStatus.DONE && instant > due;
}

/// <summary>
/// One immutable audit row per accepted change to an activity.
/// </summary>
/// <remarks>
/// Required by the shift-handover bulk update feature: "an audit entry per updated task". Audit
/// rows are append-only and owned by the activities module; nothing mutates or deletes them.
/// </remarks>
public sealed record ActivityAuditEntry(
    string Id,
    string ActivityId,
    string StoreId,
    string ActorStaffId,
    string Action,
    string? FromStatus,
    string? ToStatus,
    string? Reason,
    string Source,
    DateTimeOffset RecordedAt);

/// <summary>Filter for listing activities. Every field is optional; store scope is not.</summary>
public sealed record ActivityListFilter(
    string StoreId,
    string? ProgrammeId = null,
    ActivityStatus? Status = null);

/// <summary>
/// Read-only projection of an activity, exposed to other modules through
/// <see cref="IActivityService"/>. Consumers such as reports never see the entity itself.
/// </summary>
public sealed record ActivityReadModel(
    string Id,
    string StoreId,
    string? ProgrammeId,
    string Status,
    string Priority,
    string Category,
    string Department,
    string? AssigneeStaffId,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    bool IsOverdue);
