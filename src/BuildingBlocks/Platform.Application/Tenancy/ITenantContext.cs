namespace Platform.Application.Tenancy;

/// <summary>The tenant the current request / job operates on.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    bool HasTenant => TenantId.HasValue;

    /// <summary>Tenant id, or throws if the request is not tenant-scoped.</summary>
    Guid RequiredTenantId =>
        TenantId ?? throw new InvalidOperationException("No tenant is resolved for the current context.");
}

/// <summary>Allows background jobs and the tenant resolution middleware to set the tenant.</summary>
public interface ITenantContextSetter
{
    void SetTenant(Guid? tenantId);
}

/// <summary>Resolves tenants for anonymous traffic (public website, mobile app before login).</summary>
public interface ITenantLookup
{
    /// <summary>Accepts a tenant slug or id.</summary>
    Task<Guid?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Resolves a custom domain such as <c>www.gracechurch.org</c>.</summary>
    Task<Guid?> FindByHostAsync(string host, CancellationToken cancellationToken);
}
