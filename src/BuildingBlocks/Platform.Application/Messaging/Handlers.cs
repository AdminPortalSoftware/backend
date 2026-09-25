using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;

namespace Platform.Application.Messaging;

/// <summary>
/// Use-case handlers. Each command/query is a single class with a single responsibility.
/// Endpoints resolve handlers directly from DI — no reflection-based mediator in the hot path.
/// </summary>
public interface ICommandHandler<in TCommand>
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to a domain or integration event delivered from an outbox. Handlers MUST be idempotent:
/// delivery is at-least-once.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent @event, CancellationToken cancellationToken);
}
