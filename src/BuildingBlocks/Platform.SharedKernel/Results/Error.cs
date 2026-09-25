namespace Platform.SharedKernel.Results;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
}

/// <summary>
/// Machine-readable error. <see cref="Code"/> is stable (clients may switch on it),
/// <see cref="Description"/> is human-readable.
/// </summary>
public sealed record Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    public IReadOnlyDictionary<string, string[]>? Details { get; init; }

    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Failure(string code, string description) => new(code, description);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static Error Validation(string code, string description, IReadOnlyDictionary<string, string[]>? details = null) =>
        new(code, description, ErrorType.Validation) { Details = details };
}
