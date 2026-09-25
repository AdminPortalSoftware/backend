using Platform.Application.Tenancy;

namespace Platform.Infrastructure.Tenancy;

/// <summary>Scoped holder of the current tenant. Set once per request/job by resolution middleware.</summary>
internal sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid? tenantId) => TenantId = tenantId;
}
