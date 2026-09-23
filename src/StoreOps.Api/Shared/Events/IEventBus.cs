namespace StoreOps.Api.Shared.Events;

/// <summary>
/// The only sanctioned channel for side effects that cross a StoreOps module boundary.
/// </summary>
/// <remarks>
/// StoreOps architecture rule "Event bus only": when something that happens in module A must
/// cause work in module B, module A publishes a <see cref="DomainEvent"/> and module B
/// subscribes. Module A must not take a dependency on module B's service to do it. Concretely:
/// the activities module never references <c>IAlertService</c>; it emits
/// <c>ActivitySlaBreachedEvent</c> and the alerts module reacts.
/// </remarks>
public interface IEventBus
{
    /// <summary>Publishes an event to every handler registered for its concrete type.</summary>
    Task PublishAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>Registers a handler invoked for every published event of type <typeparamref name="TEvent"/>.</summary>
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : DomainEvent;
}
