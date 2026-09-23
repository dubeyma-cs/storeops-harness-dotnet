using StoreOps.Api.Shared.Auth;

namespace StoreOps.Api.Shared.Events;

/// <summary>
/// Base class for a module's event-bus wiring.
/// </summary>
/// <remarks>
/// Subscriptions are registered in <see cref="StartAsync"/>, which the host runs before the
/// server accepts requests — so no published event can ever arrive before its subscriber exists.
/// <para>
/// Each handler runs in its own DI scope with an explicit system identity, because a handler is
/// not part of the publishing request: it must not inherit the publisher's store scope or its
/// cancellation token semantics. This is the seam that keeps the alerts and reports modules
/// independent of whoever triggered them.
/// </para>
/// </remarks>
public abstract class EventSubscriberService : IHostedService
{
    private readonly IEventBus _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    protected EventSubscriberService(IEventBus eventBus, IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _eventBus = eventBus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>The identity handlers of this module run under.</summary>
    protected abstract StaffContext HandlerIdentity { get; }

    /// <summary>Called once at startup; register handlers with <see cref="On{TEvent}"/>.</summary>
    protected abstract void Configure();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Configure();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Registers a scoped handler for <typeparamref name="TEvent"/>.</summary>
    protected void On<TEvent>(Func<IServiceProvider, TEvent, CancellationToken, Task> handler)
        where TEvent : DomainEvent
    {
        _eventBus.Subscribe<TEvent>(async (domainEvent, cancellationToken) =>
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IStaffContextAccessor>().Set(HandlerIdentity);

            _logger.LogDebug(
                "Handling {EventName} ({EventId}) as {Identity}",
                domainEvent.Name,
                domainEvent.EventId,
                HandlerIdentity.StaffId);

            await handler(scope.ServiceProvider, domainEvent, cancellationToken);
        });
    }
}
