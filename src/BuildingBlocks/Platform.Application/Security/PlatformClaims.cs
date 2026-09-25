namespace Platform.Application.Security;

/// <summary>Claim types issued in platform access tokens. Kept short to keep tokens small.</summary>
public static class PlatformClaims
{
    public const string UserId = "sub";
    public const string TenantId = "tid";
    public const string MembershipId = "mid";
    public const string SessionId = "sid";
    public const string Email = "email";
    public const string Name = "name";
    public const string ClientType = "cty";
    public const string PlatformAdmin = "padm";

    /// <summary>Permission granted directly to a principal (used by API keys).</summary>
    public const string Scope = "scope";
}
