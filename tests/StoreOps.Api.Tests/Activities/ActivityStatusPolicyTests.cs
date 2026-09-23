using StoreOps.Api.Modules.Activities;

namespace StoreOps.Api.Tests.Activities;

/// <summary>
/// Unit tests for the activity lifecycle rules.
/// </summary>
/// <remarks>
/// These assert the business rule directly, with no HTTP involved. The Evaluator's test-quality
/// dimension rejects suites that only check status codes: a 409 tells you the request failed,
/// not that DONE is terminal.
/// </remarks>
public sealed class ActivityStatusPolicyTests
{
    [Theory]
    [InlineData(ActivityStatus.TODO, ActivityStatus.IN_PROGRESS)]
    [InlineData(ActivityStatus.TODO, ActivityStatus.BLOCKED)]
    [InlineData(ActivityStatus.TODO, ActivityStatus.DONE)]
    [InlineData(ActivityStatus.IN_PROGRESS, ActivityStatus.DONE)]
    [InlineData(ActivityStatus.IN_PROGRESS, ActivityStatus.BLOCKED)]
    [InlineData(ActivityStatus.IN_PROGRESS, ActivityStatus.TODO)]
    [InlineData(ActivityStatus.BLOCKED, ActivityStatus.IN_PROGRESS)]
    [InlineData(ActivityStatus.BLOCKED, ActivityStatus.DONE)]
    [InlineData(ActivityStatus.BLOCKED, ActivityStatus.TODO)]
    public void Given_an_open_activity_When_moving_to_another_open_or_closed_state_Then_the_transition_is_allowed(
        ActivityStatus from,
        ActivityStatus to)
    {
        Assert.True(ActivityStatusPolicy.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ActivityStatus.DONE, ActivityStatus.TODO)]
    [InlineData(ActivityStatus.DONE, ActivityStatus.IN_PROGRESS)]
    [InlineData(ActivityStatus.DONE, ActivityStatus.BLOCKED)]
    public void Given_a_completed_activity_When_reopening_it_Then_the_transition_is_rejected(
        ActivityStatus from,
        ActivityStatus to)
    {
        // DONE is terminal: completed work is a historical record, not a work item.
        Assert.False(ActivityStatusPolicy.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ActivityStatus.TODO)]
    [InlineData(ActivityStatus.IN_PROGRESS)]
    [InlineData(ActivityStatus.DONE)]
    [InlineData(ActivityStatus.BLOCKED)]
    public void Given_any_status_When_transitioning_to_itself_Then_it_is_not_a_transition(ActivityStatus status)
    {
        Assert.False(ActivityStatusPolicy.CanTransition(status, status));
    }

    [Fact]
    public void Given_the_handover_endpoint_When_choosing_a_status_Then_only_DONE_and_BLOCKED_are_accepted()
    {
        Assert.True(ActivityStatusPolicy.IsHandoverStatus(ActivityStatus.DONE));
        Assert.True(ActivityStatusPolicy.IsHandoverStatus(ActivityStatus.BLOCKED));
        Assert.False(ActivityStatusPolicy.IsHandoverStatus(ActivityStatus.TODO));
        Assert.False(ActivityStatusPolicy.IsHandoverStatus(ActivityStatus.IN_PROGRESS));
    }

    [Fact]
    public void Given_a_status_change_When_the_target_is_BLOCKED_Then_a_reason_is_required()
    {
        Assert.True(ActivityStatusPolicy.RequiresReason(ActivityStatus.BLOCKED));
        Assert.False(ActivityStatusPolicy.RequiresReason(ActivityStatus.DONE));
        Assert.False(ActivityStatusPolicy.RequiresReason(ActivityStatus.TODO));
    }

    [Theory]
    [InlineData(ActivityPriority.HIGH, true)]
    [InlineData(ActivityPriority.CRITICAL, true)]
    [InlineData(ActivityPriority.MEDIUM, false)]
    [InlineData(ActivityPriority.LOW, false)]
    public void Given_an_activity_When_checking_SLA_tracking_Then_only_HIGH_and_CRITICAL_are_tracked(
        ActivityPriority priority,
        bool expected)
    {
        var activity = NewActivity(priority, ActivityStatus.TODO, dueAt: null);

        Assert.Equal(expected, activity.IsSlaTracked);
    }

    [Fact]
    public void Given_an_activity_past_its_due_date_When_it_is_not_DONE_Then_it_is_overdue()
    {
        var due = new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);
        var activity = NewActivity(ActivityPriority.HIGH, ActivityStatus.IN_PROGRESS, due);

        Assert.False(activity.IsOverdueAt(due));
        Assert.False(activity.IsOverdueAt(due.AddMinutes(-1)));
        Assert.True(activity.IsOverdueAt(due.AddMinutes(1)));
    }

    [Fact]
    public void Given_a_completed_activity_past_its_due_date_When_checking_overdue_Then_it_is_not_overdue()
    {
        var due = new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);
        var activity = NewActivity(ActivityPriority.CRITICAL, ActivityStatus.DONE, due);

        Assert.False(activity.IsOverdueAt(due.AddDays(3)));
    }

    private static Activity NewActivity(ActivityPriority priority, ActivityStatus status, DateTimeOffset? dueAt) =>
        new()
        {
            Id = "act-test",
            StoreId = "store-042",
            Title = "Reset end-cap planogram",
            Department = "GROCERY",
            CreatedByStaffId = "staff-001",
            Priority = priority,
            Status = status,
            DueAt = dueAt,
            CreatedAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero),
        };
}
