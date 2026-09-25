using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Application.Security;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Infrastructure;

namespace Platform.Modules.Identity.Services;

/// <summary>
/// Authenticates <c>X-Api-Key: pk_{prefix}_{secret}</c>. The principal carries the tenant and
/// the key's scopes as permissions; it has no user id.
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IdentityDbContext db,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.ToString() is not { Length: > 0 } key)
        {
            return AuthenticateResult.NoResult();
        }

        var parts = key.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "pk")
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var now = clock.GetUtcNow();
        var apiKey = await db.ApiKeys.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(k => k.Prefix == parts[1], Context.RequestAborted);
        if (apiKey is null || !apiKey.IsUsable(now) || !SecretHasher.FixedTimeEquals(apiKey.KeyHash, SecretHasher.Hash(key)))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        // Throttle last-used writes to one per minute per key.
        if (apiKey.LastUsedAt is null || now - apiKey.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            await db.ApiKeys.IgnoreQueryFilters([QueryFilters.Tenant]).Where(k => k.Id == apiKey.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), Context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(PlatformClaims.TenantId, apiKey.TenantId.ToString()),
            new(PlatformClaims.Name, $"api-key:{apiKey.Name}"),
            new("api_key_id", apiKey.Id.ToString()),
        };
        claims.AddRange(apiKey.Scopes.Select(s => new Claim(PlatformClaims.Scope, s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, PlatformClaims.Name, "role"));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
