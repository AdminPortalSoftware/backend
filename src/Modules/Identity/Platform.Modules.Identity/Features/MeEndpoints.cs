using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Identity.Features;

public sealed record MeResponse(
    Guid Id, string Email, bool EmailConfirmed, string FirstName, string LastName, string? PhoneNumber, string? AvatarUrl,
    bool TwoFactorEnabled, bool IsPlatformAdmin, DateTimeOffset? LastLoginAt,
    Guid? TenantId, Guid? MembershipId, Guid? PersonId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions,
    IReadOnlyList<MyTenantResponse> Tenants);

public sealed record MyTenantResponse(Guid TenantId, Guid MembershipId, string Status);

public sealed record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber, string? AvatarUrl);

public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri);

public sealed record EnableTwoFactorRequest(string Code);

public sealed record DisableTwoFactorRequest(string Password);

public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record SessionResponse(
    Guid Id, string ClientType, string? DeviceName, string? IpAddress, string? UserAgent,
    DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, DateTimeOffset ExpiresAt, bool IsCurrent);

internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.AvatarUrl).MaximumLength(1024);
    }
}

/// <summary>The signed-in user's own account: profile, permissions, 2FA and devices.</summary>
public static class MeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/me").WithTags("My account").RequireAuthorization();

        group.MapGet("/", GetMe).WithSummary("Current user, active organisation, roles and effective permissions");
        group.MapPut("/", UpdateProfile).WithValidation<UpdateProfileRequest>().WithSummary("Update my profile");

        group.MapPost("/two-factor/setup", SetupTwoFactor).WithSummary("Start authenticator-app setup");
        group.MapPost("/two-factor/enable", EnableTwoFactor).WithSummary("Verify a code, enable 2FA and receive recovery codes");
        group.MapPost("/two-factor/disable", DisableTwoFactor).WithSummary("Disable 2FA (requires password)");

        group.MapGet("/sessions", ListSessions).WithSummary("Signed-in devices");
        group.MapDelete("/sessions/{id:guid}", RevokeSession).WithSummary("Sign out a device");
    }

    private static async Task<IResult> GetMe(
        ICurrentUser currentUser, ITenantContext tenant, IdentityDbContext db, IPermissionService permissions, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        var memberships = await db.Memberships.AsNoTracking().IgnoreQueryFilters([QueryFilters.Tenant])
            .Include(m => m.Roles)
            .Where(m => m.UserId == user.Id && m.Status != MembershipStatus.Suspended)
            .ToListAsync(ct);

        var current = memberships.FirstOrDefault(m => m.Id == currentUser.MembershipId);
        IReadOnlyList<string> roleNames = [];
        IReadOnlyList<string> granted = [];
        if (current is not null && tenant.TenantId is { } tenantId)
        {
            var roleIds = current.Roles.Select(r => r.RoleId).ToList();
            roleNames = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync(ct);
            granted = (await permissions.GetPermissionsAsync(user.Id, tenantId, ct)).Order().ToList();
        }

        return Results.Ok(new MeResponse(
            user.Id, user.Email, user.EmailConfirmed, user.FirstName, user.LastName, user.PhoneNumber, user.AvatarUrl,
            user.TwoFactorEnabled, user.IsPlatformAdmin, user.LastLoginAt,
            current?.TenantId, current?.Id, current?.PersonId, roleNames, granted,
            memberships.Select(m => new MyTenantResponse(m.TenantId, m.Id, m.Status.ToString())).ToList()));
    }

    private static async Task<IResult> UpdateProfile(UpdateProfileRequest request, ICurrentUser currentUser, IdentityDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        user.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber, request.AvatarUrl);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetupTwoFactor(ICurrentUser currentUser, IdentityDbContext db, TotpService totp, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        if (user.TwoFactorEnabled)
        {
            return Error.Conflict("auth.two_factor_already_enabled", "Two-factor authentication is already enabled.").ToProblem();
        }

        var (protectedSecret, secret) = totp.GenerateSecret();
        user.BeginTwoFactorSetup(protectedSecret);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new TwoFactorSetupResponse(secret, totp.BuildOtpAuthUri("Platform", user.Email, secret)));
    }

    private static async Task<IResult> EnableTwoFactor(
        EnableTwoFactorRequest request, ICurrentUser currentUser, IdentityDbContext db, TotpService totp, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        if (user.TwoFactorSecret is null || !totp.Verify(user.TwoFactorSecret, request.Code ?? string.Empty))
        {
            return AuthErrors.InvalidTwoFactor.ToProblem();
        }

        var codes = TotpService.GenerateRecoveryCodes();
        user.EnableTwoFactor(codes.Select(SecretHasher.Hash));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<IResult> DisableTwoFactor(
        DisableTwoFactorRequest request, ICurrentUser currentUser, IdentityDbContext db, IPasswordHasher<User> hasher, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        if (user.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            return Error.Validation("auth.wrong_password", "The password is incorrect.").ToProblem();
        }

        user.DisableTwoFactor();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListSessions(ICurrentUser currentUser, IdentityDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == currentUser.RequiredUserId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt)
            .ToListAsync(ct);

        return Results.Ok(sessions.Select(s => new SessionResponse(
            s.Id, s.ClientType.ToString(), s.DeviceName, s.IpAddress, s.UserAgent, s.CreatedAt, s.LastUsedAt, s.ExpiresAt,
            s.Id == currentUser.SessionId)));
    }

    private static async Task<IResult> RevokeSession(Guid id, ICurrentUser currentUser, IdentityDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == currentUser.RequiredUserId, ct);
        if (session is null)
        {
            return Error.NotFound("session.not_found", "Session not found.").ToProblem();
        }

        session.Revoke(clock.GetUtcNow(), "revoked_by_user");
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
