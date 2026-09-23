namespace StoreOps.Api.Modules.Alerts;

/// <summary>How an alert reaches the recipient.</summary>
public enum NotificationChannel
{
    IN_APP = 0,
    EMAIL = 1,
}

/// <summary>Delivery state of an alert.</summary>
public enum NotificationStatus
{
    PENDING = 0,
    SENT = 1,
    READ = 2,
    FAILED = 3,
}

/// <summary>The operational event an alert reports.</summary>
public enum AlertType
{
    INVENTORY = 0,
    SLA_BREACH = 1,
    SHIFT_HANDOVER = 2,
    ESCALATION = 3,
}

/// <summary>An in-app alert addressed to one member of store staff.</summary>
public sealed record Notification
{
    public required string Id { get; init; }

    public required string StoreId { get; init; }

    public required string RecipientStaffId { get; init; }

    public required AlertType Type { get; init; }

    public NotificationChannel Channel { get; init; } = NotificationChannel.IN_APP;

    public NotificationStatus Status { get; init; } = NotificationStatus.PENDING;

    public required string Title { get; init; }

    public required string Body { get; init; }

    public string? RelatedActivityId { get; init; }

    public string? RelatedProgrammeId { get; init; }

    /// <summary>Id of the domain event that caused this alert — the audit link back to the bus.</summary>
    public required string SourceEventId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; init; }
}

/// <summary>
/// A breach awaiting escalation. Created when an SLA breach alert is raised; resolved when the
/// activity reaches DONE, or escalated to store management once the grace period lapses.
/// </summary>
public sealed record PendingEscalation(
    string ActivityId,
    string StoreId,
    string? DepartmentLeadStaffId,
    DateTimeOffset BreachedAt,
    DateTimeOffset EscalateAfter);
