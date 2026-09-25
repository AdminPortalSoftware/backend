namespace Platform.SharedKernel.Domain;

/// <summary>Tenant-scoped aggregate with auditing and soft delete — the default for business data.</summary>
public abstract class TenantAggregateRoot : AggregateRoot, ITenantOwned, IAuditable, ISoftDeletable
{
    public Guid TenantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    public void AssignTenant(Guid tenantId) => TenantGuard.Assign(TenantId, tenantId, v => TenantId = v);
}

/// <summary>Tenant-scoped child entity (owned by an aggregate) with auditing.</summary>
public abstract class TenantEntity : Entity, ITenantOwned, IAuditable
{
    public Guid TenantId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void AssignTenant(Guid tenantId) => TenantGuard.Assign(TenantId, tenantId, v => TenantId = v);
}

/// <summary>Global (not tenant-scoped) aggregate with auditing, e.g. the tenant registry itself.</summary>
public abstract class AuditableAggregateRoot : AggregateRoot, IAuditable
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
}

internal static class TenantGuard
{
    public static void Assign(Guid current, Guid next, Action<Guid> set)
    {
        if (next == Guid.Empty)
        {
            throw new DomainException("A tenant id is required.");
        }

        if (current != Guid.Empty && current != next)
        {
            throw new DomainException("An entity cannot be moved between tenants.");
        }

        set(next);
    }
}
