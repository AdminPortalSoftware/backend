namespace Platform.Infrastructure.Auditing;

/// <summary>Ambient request metadata recorded with audit entries and outbox messages.</summary>
public interface IRequestInfo
{
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}

internal sealed class NullRequestInfo : IRequestInfo
{
    public string? IpAddress => null;
    public string? UserAgent => null;
    public string? CorrelationId => null;
}
