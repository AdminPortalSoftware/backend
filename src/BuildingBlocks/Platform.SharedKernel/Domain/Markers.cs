namespace Platform.SharedKernel.Domain;

/// <summary>
/// Row belongs to a single tenant (church / organisation). The tenant is stamped automatically
/// on insert and enforced by a global query filter, so application code never filters by hand.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
    void AssignTenant(Guid tenantId);
}

/// <summary>Creation / modification metadata, populated by the AuditableInterceptor.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }
    Guid? CreatedBy { get; }
    DateTimeOffset? UpdatedAt { get; }
    Guid? UpdatedBy { get; }
}

/// <summary>Deletes are converted to updates and the row is hidden by a global query filter.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    Guid? DeletedBy { get; }
}

/// <summary>Excludes a property's values from the audit trail (passwords, secrets, tokens).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;
