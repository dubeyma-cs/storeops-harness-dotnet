using System.Collections.Concurrent;

namespace StoreOps.Api.Shared.Events;

/// <summary>
/// Process-local <see cref="IEventBus"/> implementation used by the capstone reference build.
/// </summary>
/// <remarks>
/// Deliberately simple: handlers run in-process, sequentially, and a handler that throws is
/// logged and swallowed so a failing subscriber in the alerts module can never fail the
/// publishing request in the activities module. That isolation is the point of the bus — swap
/// this for Azure Service Bus and the module code does not change.
/// </remarks>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Func<DomainEvent, CancellationToken, Task>>> _handlers = new();
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
    }

    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : DomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Func<DomainEvent, CancellationToken, Task>>());

        lock (handlers)
        {
            handlers.Add((domainEvent, token) => handler((TEvent)domainEvent, token));
        }
    }

    public async Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (!_handlers.TryGetValue(domainEvent.GetType(), out var handlers))
        {
            _logger.LogDebug(
                "Event {EventName} published from {SourceModule} with no subscribers",
                domainEvent.Name,
                domainEvent.SourceModule);
            return;
        }

        Func<DomainEvent, CancellationToken, Task>[] snapshot;
        lock (handlers)
        {
            snapshot = handlers.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Subscriber failed handling {EventName} ({EventId}) from {SourceModule}",
                    domainEvent.Name,
                    domainEvent.EventId,
                    domainEvent.SourceModule);
            }
        }
    }
}
