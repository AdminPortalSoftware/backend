namespace Platform.Infrastructure.Caching;

/// <summary>Cache key and tag conventions. Tags enable bulk invalidation via HybridCache.RemoveByTagAsync.</summary>
public static class CacheKeys
{
    public static string TenantTag(Guid tenantId) => $"tenant:{tenantId:N}";

    public static string Permissions(Guid tenantId, Guid userId) => $"perm:{tenantId:N}:{userId:N}";

    public static string PermissionsTag(Guid tenantId) => $"perm:{tenantId:N}";

    public static string TenantBySlug(string slug) => $"tenant-slug:{slug}";

    public static string TenantByHost(string host) => $"tenant-host:{host}";
}
