namespace StoreOps.Api.Shared.Events;

/// <summary>Base type for every cross-module StoreOps event.</summary>
public abstract record DomainEvent
{
    /// <summary>Stable event name used in logs and in the run-log audit trail.</summary>
    public abstract string Name { get; }

    /// <summary>Correlation id so a chain of events raised by one request can be reconstructed.</summary>
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>When the originating module raised the event.</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Module that published the event (activities, programmes, staff, alerts, reports).</summary>
    public abstract string SourceModule { get; }
}
