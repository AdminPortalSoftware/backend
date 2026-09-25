using System.ComponentModel.DataAnnotations;

namespace Platform.Modules.Identity.Services;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Required] public string Issuer { get; set; } = "platform";
    [Required] public string Audience { get; set; } = "platform-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply via secret store / environment in production.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = null!;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Refresh token idle timeout — renewed on each use.</summary>
    public TimeSpan RefreshTokenSlidingLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Hard cap on a session regardless of activity.</summary>
    public TimeSpan RefreshTokenAbsoluteLifetime { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Concurrent refreshes (e.g. two mobile requests) within this window are not treated as theft.</summary>
    public TimeSpan RefreshReuseGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxFailedAccessAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Base URL of the admin/website app used in email links.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:3000";

    public string RefreshCookieName { get; set; } = "rt";
}
