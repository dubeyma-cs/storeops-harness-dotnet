using Microsoft.Extensions.Logging.Abstractions;
using StoreOps.Api.Shared.Events;

namespace StoreOps.Api.Tests.Shared;

/// <summary>
/// Unit tests for the event bus contract that the module boundary rules depend on.
/// </summary>
public sealed class EventBusTests
{
    private static InMemoryEventBus NewBus() => new(NullLogger<InMemoryEventBus>.Instance);

    [Fact]
    public async Task Given_a_subscriber_When_a_matching_event_is_published_Then_the_handler_receives_it()
    {
        var bus = NewBus();
        ActivityStatusChangedEvent? received = null;

        bus.Subscribe<ActivityStatusChangedEvent>((e, _) =>
        {
            received = e;
            return Task.CompletedTask;
        });

        var published = NewStatusChanged("act-1");
        await bus.PublishAsync(published);

        Assert.NotNull(received);
        Assert.Equal(published.EventId, received!.EventId);
        Assert.Equal("activity.status-changed", received.Name);
        Assert.Equal("activities", received.SourceModule);
    }

    [Fact]
    public async Task Given_several_subscribers_When_an_event_is_published_Then_all_of_them_run()
    {
        var bus = NewBus();
        var calls = 0;

        bus.Subscribe<ActivityStatusChangedEvent>((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        bus.Subscribe<ActivityStatusChangedEvent>((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(NewStatusChanged("act-2"));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Given_a_subscriber_for_another_event_type_When_publishing_Then_it_is_not_invoked()
    {
        var bus = NewBus();
        var invoked = false;

        bus.Subscribe<ProgrammeClosedEvent>((_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(NewStatusChanged("act-3"));

        Assert.False(invoked);
    }

    [Fact]
    public async Task Given_no_subscribers_When_publishing_Then_the_publisher_is_unaffected()
    {
        var bus = NewBus();

        await bus.PublishAsync(NewStatusChanged("act-4"));
    }

    [Fact]
    public async Task Given_a_failing_subscriber_When_publishing_Then_the_publisher_still_succeeds()
    {
        var bus = NewBus();
        var secondRan = false;

        bus.Subscribe<ActivityStatusChangedEvent>((_, _) =>
            throw new InvalidOperationException("alerts module is having a bad day"));

        bus.Subscribe<ActivityStatusChangedEvent>((_, _) =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });

        // A broken subscriber in one module must not fail the request that published the event,
        // and must not stop other subscribers.
        await bus.PublishAsync(NewStatusChanged("act-5"));

        Assert.True(secondRan);
    }

    [Fact]
    public void Given_a_null_handler_When_subscribing_Then_it_is_rejected()
    {
        var bus = NewBus();

        Assert.Throws<ArgumentNullException>(() =>
            bus.Subscribe<ActivityStatusChangedEvent>(null!));
    }

    [Fact]
    public async Task Given_a_null_event_When_publishing_Then_it_is_rejected()
    {
        var bus = NewBus();

        await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync(null!));
    }

    [Fact]
    public void Given_two_events_When_they_are_created_Then_each_has_its_own_correlation_id()
    {
        var first = NewStatusChanged("act-6");
        var second = NewStatusChanged("act-6");

        Assert.NotEqual(first.EventId, second.EventId);
    }

    private static ActivityStatusChangedEvent NewStatusChanged(string activityId) => new(
        ActivityId: activityId,
        StoreId: "store-042",
        FromStatus: "TODO",
        ToStatus: "DONE",
        Priority: "HIGH",
        Category: "AUDIT",
        AssigneeId: "staff-003",
        ChangedByStaffId: "staff-002",
        Reason: "shift handover");
}
