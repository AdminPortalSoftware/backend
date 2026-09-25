using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

/// <summary>
/// Server-to-server credential for integrations (website build/SSR, partner systems).
/// Format: <c>pk_{prefix}_{secret}</c>; only a SHA-256 hash of the full key is stored.
/// </summary>
public sealed class ApiKey : TenantAggregateRoot
{
    private ApiKey() { }

    public string Name { get; private set; } = null!;

    /// <summary>Public, unique lookup part — safe to show in the UI.</summary>
    public string Prefix { get; private set; } = null!;

    [AuditIgnore] public string KeyHash { get; private set; } = null!;

    public List<string> Scopes { get; private set; } = [];
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ApiKey Create(string name, string prefix, string keyHash, IEnumerable<string> scopes, DateTimeOffset? expiresAt) => new()
    {
        Name = name,
        Prefix = prefix,
        KeyHash = keyHash,
        Scopes = scopes.Distinct(StringComparer.Ordinal).ToList(),
        ExpiresAt = expiresAt,
    };

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
