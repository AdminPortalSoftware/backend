using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

public enum ClientType
{
    Admin,
    Web,
    Mobile,
    Other,
}

/// <summary>
/// A signed-in device. Backs the refresh token, which is rotated on every use. Presenting an
/// already-rotated token (outside a short grace window) is treated as theft: the session is revoked.
/// </summary>
public sealed class UserSession : Entity
{
    private UserSession() { }

    public Guid UserId { get; private set; }
    /// <summary>Null for platform-operator sessions that are not bound to a tenant.</summary>
    public Guid? TenantId { get; private set; }
    public Guid? MembershipId { get; private set; }
    public ClientType ClientType { get; private set; }
    public string? DeviceName { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    [AuditIgnore] public string TokenHash { get; private set; } = null!;
    [AuditIgnore] public string? PreviousTokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public static UserSession Start(
        Guid userId, Guid? tenantId, Guid? membershipId, ClientType clientType, string tokenHash,
        DateTimeOffset now, TimeSpan lifetime, string? deviceName, string? ipAddress, string? userAgent) => new()
    {
        UserId = userId,
        TenantId = tenantId,
        MembershipId = membershipId,
        ClientType = clientType,
        TokenHash = tokenHash,
        CreatedAt = now,
        LastUsedAt = now,
        ExpiresAt = now.Add(lifetime),
        DeviceName = deviceName,
        IpAddress = ipAddress,
        UserAgent = userAgent,
    };

    public void Rotate(string newTokenHash, DateTimeOffset now, TimeSpan slidingLifetime, DateTimeOffset absoluteExpiry, string? ipAddress)
    {
        PreviousTokenHash = TokenHash;
        TokenHash = newTokenHash;
        RotatedAt = now;
        LastUsedAt = now;
        IpAddress = ipAddress ?? IpAddress;
        var sliding = now.Add(slidingLifetime);
        ExpiresAt = sliding < absoluteExpiry ? sliding : absoluteExpiry;
    }

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
