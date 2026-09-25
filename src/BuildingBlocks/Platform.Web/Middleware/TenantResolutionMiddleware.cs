using Microsoft.AspNetCore.Http;
using Platform.Application.Security;
using Platform.Application.Tenancy;

namespace Platform.Web.Middleware;

/// <summary>
/// Resolves the tenant for the request, in priority order:
/// 1. the <c>tid</c> claim of an authenticated principal (cannot be overridden),
/// 2. the <c>X-Tenant</c> header (slug or id) — website SSR, mobile app,
/// 3. the request host (custom domains such as www.gracechurch.org).
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantHeader = "X-Tenant";

    public async Task InvokeAsync(HttpContext context, ITenantContextSetter setter, ITenantLookup lookup)
    {
        setter.SetTenant(await ResolveAsync(context, lookup));
        await next(context);
    }

    private static async Task<Guid?> ResolveAsync(HttpContext context, ITenantLookup lookup)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return Guid.TryParse(context.User.FindFirst(PlatformClaims.TenantId)?.Value, out var claimed) ? claimed : null;
        }

        var ct = context.RequestAborted;
        if (context.Request.Headers.TryGetValue(TenantHeader, out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return await lookup.FindByIdentifierAsync(header.ToString().Trim(), ct);
        }

        return await lookup.FindByHostAsync(context.Request.Host.Host, ct);
    }
}
