using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Application.Security;
using Platform.Infrastructure.Caching;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Effective permissions = union of the user's role permissions in the tenant. Cached per
/// user and tagged per tenant; any role or membership change invalidates the tenant tag.
/// </summary>
internal sealed class PermissionService(IdentityDbContext db, HybridCache cache) : IPermissionService
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var permissions = await cache.GetOrCreateAsync(
            CacheKeys.Permissions(tenantId, userId),
            async ct => await LoadAsync(userId, tenantId, ct),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) },
            tags: [CacheKeys.PermissionsTag(tenantId)],
            cancellationToken: cancellationToken);

        return permissions.ToHashSet(StringComparer.Ordinal);
    }

    public async Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await cache.RemoveByTagAsync(CacheKeys.PermissionsTag(tenantId), cancellationToken);

    private async Task<string[]> LoadAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var roleIds = db.Memberships
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.TenantId == tenantId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .SelectMany(m => m.Roles.Select(r => r.RoleId));

        var permissionLists = await db.Roles
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id))
            .Select(r => r.Permissions)
            .ToListAsync(ct);

        return permissionLists.SelectMany(p => p).Distinct(StringComparer.Ordinal).ToArray();
    }
}
