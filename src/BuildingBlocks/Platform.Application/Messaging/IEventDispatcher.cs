using Platform.SharedKernel.Domain;

namespace Platform.Application.Messaging;

/// <summary>
/// Delivers an event to every registered <see cref="IEventHandler{TEvent}"/>.
/// In-process today; swap the implementation for a broker (RabbitMQ, Azure Service Bus)
/// when a module is extracted into its own service — handlers do not change.
/// </summary>
public interface IEventDispatcher
{
    Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken);
}
