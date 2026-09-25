using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Infrastructure.Caching;
using Platform.Modules.Tenancy.Contracts;

namespace Platform.Modules.Tenancy.Infrastructure;

internal sealed class TenantDirectory(TenancyDbContext db, HybridCache cache) : ITenantDirectory
{
    public async Task<TenantSummary?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            $"tenant-summary:{tenantId:N}",
            async ct => await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new TenantSummary(t.Id, t.Slug, t.Name, t.Status.ToString(), t.DefaultCurrency, t.TimeZone, t.DefaultLocale))
                .FirstOrDefaultAsync(ct),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
            tags: [CacheKeys.TenantTag(tenantId)],
            cancellationToken: cancellationToken);
}
