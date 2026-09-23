using System.ComponentModel.DataAnnotations;

namespace StoreOps.Api.Modules.Activities;

/// <summary>Request body for <c>POST /api/activities</c>.</summary>
public sealed class CreateActivityRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(140, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(40, MinimumLength = 2)]
    public string Department { get; set; } = string.Empty;

    public string? ProgrammeId { get; set; }

    public ActivityPriority Priority { get; set; } = ActivityPriority.MEDIUM;

    public ActivityCategory Category { get; set; } = ActivityCategory.GENERAL;

    public string? AssigneeStaffId { get; set; }

    public DateTimeOffset? DueAt { get; set; }
}

/// <summary>Request body for <c>PATCH /api/activities/{id}</c>. Omitted fields are left unchanged.</summary>
public sealed class UpdateActivityRequest
{
    public ActivityStatus? Status { get; set; }

    public ActivityPriority? Priority { get; set; }

    public ActivityCategory? Category { get; set; }

    public string? AssigneeStaffId { get; set; }

    [StringLength(280)]
    public string? Reason { get; set; }
}

/// <summary>One item of a shift-handover bulk status request.</summary>
public sealed class BulkStatusItem
{
    [Required(AllowEmptyStrings = false)]
    public string ActivityId { get; set; } = string.Empty;

    [Required]
    public ActivityStatus Status { get; set; } = ActivityStatus.DONE;

    [StringLength(280)]
    public string? Reason { get; set; }
}

/// <summary>
/// Request body for <c>PATCH /api/activities/bulk-status</c> — the shift-handover feature
/// produced by the harness demonstration run.
/// </summary>
public sealed class BulkStatusUpdateRequest
{
    /// <summary>Maximum activities accepted in one handover request.</summary>
    public const int MaxItems = 100;

    [Required]
    [MinLength(1)]
    [MaxLength(MaxItems)]
    public List<BulkStatusItem> Items { get; set; } = new();
}

/// <summary>Per-item outcome of a bulk status request.</summary>
public sealed record BulkStatusItemResult(
    string ActivityId,
    string Outcome,
    string? Status,
    string? ErrorCode,
    string? Message)
{
    public const string OutcomeUpdated = "UPDATED";
    public const string OutcomeFailed = "FAILED";

    public static BulkStatusItemResult Updated(string activityId, ActivityStatus status) =>
        new(activityId, OutcomeUpdated, status.ToString(), null, null);

    public static BulkStatusItemResult Failed(string activityId, string errorCode, string message) =>
        new(activityId, OutcomeFailed, null, errorCode, message);
}

/// <summary>
/// Response body for <c>PATCH /api/activities/bulk-status</c>. Always 207 unless every item
/// succeeded (200) or every item failed (409) — see <c>ActivitiesController</c>.
/// </summary>
public sealed record BulkStatusUpdateResponse(
    int Requested,
    int Updated,
    int Failed,
    IReadOnlyList<BulkStatusItemResult> Results);

/// <summary>Wire representation of an activity.</summary>
public sealed record ActivityResponse(
    string Id,
    string StoreId,
    string? ProgrammeId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    string Category,
    string Department,
    string? AssigneeStaffId,
    string CreatedByStaffId,
    DateTimeOffset? DueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt)
{
    /// <summary>Projects the domain entity onto the wire contract.</summary>
    public static ActivityResponse From(Activity activity) => new(
        activity.Id,
        activity.StoreId,
        activity.ProgrammeId,
        activity.Title,
        activity.Description,
        activity.Status.ToString(),
        activity.Priority.ToString(),
        activity.Category.ToString(),
        activity.Department,
        activity.AssigneeStaffId,
        activity.CreatedByStaffId,
        activity.DueAt,
        activity.CreatedAt,
        activity.UpdatedAt,
        activity.CompletedAt);
}
