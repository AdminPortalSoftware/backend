using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Application.Security;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Services;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

internal sealed class TokenService(IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(User user, TenantMembership? membership, UserSession? session, ClientType clientType)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.Add(o.AccessTokenLifetime);

        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.UserId] = user.Id.ToString(),
            [PlatformClaims.Email] = user.Email,
            [PlatformClaims.Name] = user.FullName,
            [PlatformClaims.ClientType] = clientType.ToString().ToLowerInvariant(),
        };

        if (membership is not null)
        {
            claims[PlatformClaims.TenantId] = membership.TenantId.ToString();
            claims[PlatformClaims.MembershipId] = membership.Id.ToString();
        }

        if (session is not null)
        {
            claims[PlatformClaims.SessionId] = session.Id.ToString();
        }

        if (user.IsPlatformAdmin)
        {
            claims[PlatformClaims.PlatformAdmin] = "true";
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }

    /// <summary>Short-lived token proving the password step succeeded, exchanged with a TOTP code.</summary>
    public string CreateTwoFactorChallenge(User user, Guid tenantId)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = $"{o.Audience}:2fa",
            Claims = new Dictionary<string, object>
            {
                [PlatformClaims.UserId] = user.Id.ToString(),
                [PlatformClaims.TenantId] = tenantId.ToString(),
                ["stamp"] = user.SecurityStamp,
            },
            Expires = now.AddMinutes(5).UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256),
        });
    }

    public async Task<(Guid UserId, Guid TenantId, string Stamp)?> ValidateTwoFactorChallengeAsync(string token)
    {
        var o = options.Value;
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = o.Issuer,
            ValidAudience = $"{o.Audience}:2fa",
            IssuerSigningKey = SigningKey(o),
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid ||
            !Guid.TryParse(result.Claims[PlatformClaims.UserId]?.ToString(), out var userId) ||
            !Guid.TryParse(result.Claims[PlatformClaims.TenantId]?.ToString(), out var tenantId))
        {
            return null;
        }

        return (userId, tenantId, result.Claims["stamp"]?.ToString() ?? string.Empty);
    }

    public static SymmetricSecurityKey SigningKey(AuthOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));

    public static TokenValidationParameters ValidationParameters(AuthOptions o) => new()
    {
        ValidIssuer = o.Issuer,
        ValidAudience = o.Audience,
        IssuerSigningKey = SigningKey(o),
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = PlatformClaims.Name,
        RoleClaimType = "role",
    };
}

/// <summary>Refresh token = "{sessionId}.{secret}". Only SHA-256(secret) is stored.</summary>
internal static class RefreshTokenFormat
{
    public static (string Token, string Hash) Create(Guid sessionId)
    {
        var secret = SecretHasher.NewSecret();
        return ($"{sessionId:N}.{secret}", SecretHasher.Hash(secret));
    }

    public static bool TryParse(string token, out Guid sessionId, out string secretHash)
    {
        sessionId = Guid.Empty;
        secretHash = string.Empty;
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out sessionId) || parts[1].Length < 32)
        {
            return false;
        }

        secretHash = SecretHasher.Hash(parts[1]);
        return true;
    }
}

internal static class ClaimsPrincipalExtensions
{
    public static Guid? GetGuid(this ClaimsPrincipal principal, string claim) =>
        Guid.TryParse(principal.FindFirst(claim)?.Value, out var id) ? id : null;
}
