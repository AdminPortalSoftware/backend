namespace Platform.SharedKernel.Domain;

/// <summary>
/// Thrown when an invariant is violated by programming error. Expected business failures
/// are returned as <see cref="Results.Result"/> instead of thrown.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
