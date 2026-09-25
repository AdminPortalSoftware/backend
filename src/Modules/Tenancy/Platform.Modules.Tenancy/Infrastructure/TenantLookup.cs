using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Caching;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure;

/// <summary>Cached slug/host → tenant id resolution for anonymous traffic. Suspended tenants do not resolve.</summary>
internal sealed class TenantLookup(TenancyDbContext db, HybridCache cache) : ITenantLookup
{
    private static readonly HybridCacheEntryOptions Options = new() { Expiration = TimeSpan.FromMinutes(5) };

    public async Task<Guid?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        var key = identifier.ToLowerInvariant();
        if (key.Length > 63)
        {
            return null;
        }

        var id = await cache.GetOrCreateAsync(CacheKeys.TenantBySlug(key), async ct =>
        {
            var query = db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial);
            query = Guid.TryParse(key, out var parsed) ? query.Where(t => t.Id == parsed) : query.Where(t => t.Slug == key);
            return await query.Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        }, Options, cancellationToken: cancellationToken);

        return id;
    }

    public async Task<Guid?> FindByHostAsync(string host, CancellationToken cancellationToken)
    {
        var key = host.ToLowerInvariant();
        if (key is "localhost" or "127.0.0.1" || key.Length > 253)
        {
            return null;
        }

        return await cache.GetOrCreateAsync(CacheKeys.TenantByHost(key), async ct =>
            await db.TenantDomains.AsNoTracking()
                .Where(d => d.Host == key && d.VerifiedAt != null)
                .Join(db.Tenants.Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial),
                    d => d.TenantId, t => t.Id, (d, _) => (Guid?)d.TenantId)
                .FirstOrDefaultAsync(ct), Options, cancellationToken: cancellationToken);
    }
}
