using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.SharedKernel.Results;

namespace Platform.Modules.Identity.Services;

public sealed record AuthUser(Guid Id, string Email, string FirstName, string LastName, string? AvatarUrl, bool EmailConfirmed, bool TwoFactorEnabled);

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid? TenantId,
    AuthUser User)
{
    public string TokenType => "Bearer";
}

internal static class AuthErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "The email or password is incorrect.");
    public static readonly Error LockedOut = Error.Forbidden("auth.locked_out", "Too many failed attempts. Try again later.");
    public static readonly Error NoMembership = Error.Forbidden("auth.no_membership", "Your account does not have access to this organisation.");
    public static readonly Error InvalidRefreshToken = Error.Unauthorized("auth.invalid_refresh_token", "The session has expired. Please sign in again.");
    public static readonly Error RefreshRace = Error.Conflict("auth.refresh_superseded", "This refresh token was just rotated by a concurrent request.");
    public static readonly Error InvalidTwoFactor = Error.Unauthorized("auth.invalid_two_factor", "The verification code is invalid or expired.");
    public static readonly Error InvalidToken = Error.Validation("auth.invalid_token", "The link is invalid or has expired.");
    public static readonly Error EmailTaken = Error.Conflict("auth.email_taken", "An account with this email already exists. Sign in instead.");
    public static readonly Error TenantRequired = Error.Validation("auth.tenant_required", "Specify the organisation via the X-Tenant header.");

    public static Error TenantSelectionRequired(IEnumerable<string> tenantIds) =>
        Error.Validation("auth.tenant_selection_required", "Your account belongs to several organisations. Choose one.",
            new Dictionary<string, string[]> { ["tenants"] = tenantIds.ToArray() });
}

/// <summary>Creates sessions and token pairs; shared by login, 2FA, registration, refresh and tenant switching.</summary>
internal sealed class SignInService(
    IdentityDbContext db,
    TokenService tokens,
    IPasswordHasher<User> passwordHasher,
    IOptions<AuthOptions> options,
    IRequestInfo requestInfo,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider clock)
{
    /// <summary>Hash of a random password; verified against when the user does not exist to equalise timing.</summary>
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<User>().HashPassword(null!, Guid.NewGuid().ToString()));

    public async Task<Result<User>> VerifyPasswordAsync(string email, string password, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (user?.PasswordHash is null || user.Status == UserStatus.Disabled)
        {
            passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, password);
            return AuthErrors.InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            return AuthErrors.LockedOut;
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, options.Value.MaxFailedAccessAttempts, options.Value.LockoutDuration);
            await db.SaveChangesAsync(ct);
            return AuthErrors.InvalidCredentials;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(passwordHasher.HashPassword(user, password));
        }

        return user;
    }

    /// <summary>
    /// Picks the tenant to sign into: the requested one, the only one, or asks the client to choose.
    /// Platform administrators without memberships receive a tenant-less token.
    /// </summary>
    public async Task<Result<TenantMembership?>> ResolveMembershipAsync(User user, Guid? requestedTenantId, CancellationToken ct)
    {
        var memberships = await db.Memberships
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .Include(m => m.Roles)
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active)
            .ToListAsync(ct);

        if (requestedTenantId is { } tenantId)
        {
            var match = memberships.FirstOrDefault(m => m.TenantId == tenantId);
            return match is null ? AuthErrors.NoMembership : match;
        }

        return memberships.Count switch
        {
            1 => memberships[0],
            0 when user.IsPlatformAdmin => Result.Success<TenantMembership?>(null),
            0 => AuthErrors.NoMembership,
            _ => AuthErrors.TenantSelectionRequired(memberships.Select(m => m.TenantId.ToString())),
        };
    }

    public async Task<AuthTokens> IssueAsync(User user, TenantMembership? membership, ClientType clientType, string? deviceName, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        user.RegisterSuccessfulLogin(now);

        var session = UserSession.Start(
            user.Id, membership?.TenantId, membership?.Id, clientType, tokenHash: string.Empty, now,
            options.Value.RefreshTokenSlidingLifetime, deviceName, requestInfo.IpAddress, requestInfo.UserAgent);
        var (refreshToken, hash) = RefreshTokenFormat.Create(session.Id);
        session.Rotate(hash, now, options.Value.RefreshTokenSlidingLifetime, now.Add(options.Value.RefreshTokenAbsoluteLifetime), null);

        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return Build(user, membership, session, refreshToken, clientType);
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (!RefreshTokenFormat.TryParse(refreshToken, out var sessionId, out var hash))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var now = clock.GetUtcNow();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || !session.IsActive(now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (!SecretHasher.FixedTimeEquals(session.TokenHash, hash))
        {
            if (session.PreviousTokenHash is not null && SecretHasher.FixedTimeEquals(session.PreviousTokenHash, hash))
            {
                if (session.RotatedAt is { } rotated && now - rotated <= options.Value.RefreshReuseGracePeriod)
                {
                    return AuthErrors.RefreshRace;
                }

                // A rotated token came back: assume it was stolen and kill the session.
                session.Revoke(now, "refresh_token_reuse");
                await db.SaveChangesAsync(ct);
            }

            return AuthErrors.InvalidRefreshToken;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        TenantMembership? membership = null;
        if (session.MembershipId is { } membershipId)
        {
            membership = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
                .FirstOrDefaultAsync(m => m.Id == membershipId && m.Status == MembershipStatus.Active, ct);
            if (membership is null)
            {
                session.Revoke(now, "membership_inactive");
                await db.SaveChangesAsync(ct);
                return AuthErrors.InvalidRefreshToken;
            }
        }

        var (newToken, newHash) = RefreshTokenFormat.Create(session.Id);
        session.Rotate(newHash, now, options.Value.RefreshTokenSlidingLifetime,
            session.CreatedAt.Add(options.Value.RefreshTokenAbsoluteLifetime), requestInfo.IpAddress);
        await db.SaveChangesAsync(ct);

        return Build(user, membership, session, newToken, session.ClientType);
    }

    public async Task RevokeAllSessionsAsync(Guid userId, string reason, Guid? exceptSessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSessionId).ToListAsync(ct);
        sessions.ForEach(s => s.Revoke(now, reason));
    }

    private AuthTokens Build(User user, TenantMembership? membership, UserSession session, string refreshToken, ClientType clientType)
    {
        var access = tokens.CreateAccessToken(user, membership, session, clientType);
        var result = new AuthTokens(
            access.Token, access.ExpiresAt, refreshToken, session.ExpiresAt, membership?.TenantId,
            new AuthUser(user.Id, user.Email, user.FirstName, user.LastName, user.AvatarUrl, user.EmailConfirmed, user.TwoFactorEnabled));

        // Browser clients also get the refresh token as an HttpOnly cookie (not readable by JS).
        if (clientType is ClientType.Admin or ClientType.Web && httpContextAccessor.HttpContext is { } http)
        {
            http.Response.Cookies.Append(options.Value.RefreshCookieName, refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/v1/auth",
                Expires = session.ExpiresAt,
            });
        }

        return result;
    }

    public void ClearRefreshCookie()
    {
        httpContextAccessor.HttpContext?.Response.Cookies.Delete(options.Value.RefreshCookieName, new CookieOptions { Path = "/api/v1/auth" });
    }
}
