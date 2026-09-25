namespace Platform.SharedKernel.Domain;

/// <summary>
/// Base type for every persisted entity. Identifiers are UUIDv7 (time-ordered) so they
/// are globally unique, safe to generate client-side and index-friendly in PostgreSQL.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
