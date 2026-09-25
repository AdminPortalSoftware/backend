using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Identity.Features;

public sealed record LoginRequest(string Email, string Password, ClientType ClientType = ClientType.Web, string? DeviceName = null, Guid? TenantId = null);

public sealed record LoginResponse(bool RequiresTwoFactor, string? TwoFactorToken, AuthTokens? Tokens);

public sealed record TwoFactorLoginRequest(string TwoFactorToken, string? Code, string? RecoveryCode, ClientType ClientType = ClientType.Web, string? DeviceName = null);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName, string? PhoneNumber,
    ClientType ClientType = ClientType.Mobile, string? DeviceName = null);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record AcceptInvitationRequest(string Email, string Token, string Password, ClientType ClientType = ClientType.Admin);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record SwitchTenantRequest(Guid TenantId, ClientType ClientType = ClientType.Admin);

internal static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(10).WithMessage("Use at least 10 characters.")
            .MaximumLength(128)
            .Must(p => p.Any(char.IsLetter) && p.Any(c => !char.IsLetter(c)))
            .WithMessage("Use a mix of letters and numbers or symbols.");
}

internal sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ClientType).IsInEnum();
        RuleFor(x => x.DeviceName).MaximumLength(128);
    }
}

internal sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.ClientType).IsInEnum();
    }
}

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

internal sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).StrongPassword();
    }
}

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword().NotEqual(x => x.CurrentPassword).WithMessage("Choose a different password.");
    }
}

/// <summary>
/// Authentication API shared by the admin console, website and mobile app.
/// Browser clients receive the refresh token as an HttpOnly cookie as well as in the body;
/// native clients store it in the platform keychain/keystore.
/// </summary>
public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var anonymous = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/auth")
            .WithTags("Authentication")
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        anonymous.MapPost("/login", Login).WithValidation<LoginRequest>().WithSummary("Sign in with email and password");
        anonymous.MapPost("/login/two-factor", LoginTwoFactor).WithSummary("Complete sign-in with an authenticator or recovery code");
        anonymous.MapPost("/refresh", Refresh).WithSummary("Rotate the refresh token and obtain a new access token");
        anonymous.MapPost("/register", Register).WithValidation<RegisterRequest>().WithSummary("Create a member account (website / mobile app)");
        anonymous.MapPost("/forgot-password", ForgotPassword).WithSummary("Email a password reset link");
        anonymous.MapPost("/reset-password", ResetPassword).WithValidation<ResetPasswordRequest>().WithSummary("Reset the password using an emailed token");
        anonymous.MapPost("/confirm-email", ConfirmEmail).WithSummary("Confirm an email address");
        anonymous.MapPost("/accept-invitation", AcceptInvitation).WithValidation<AcceptInvitationRequest>().WithSummary("Accept a staff invitation and set a password");

        var authenticated = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/auth")
            .WithTags("Authentication")
            .RequireAuthorization();

        authenticated.MapPost("/logout", Logout).WithSummary("Sign out of the current session");
        authenticated.MapPost("/change-password", ChangePassword).WithValidation<ChangePasswordRequest>().WithSummary("Change password and sign out other sessions");
        authenticated.MapPost("/switch-tenant", SwitchTenant).WithSummary("Obtain tokens for another organisation the user belongs to");
    }

    private static async Task<IResult> Login(
        LoginRequest request, SignInService signIn, TokenService tokens, ITenantContext tenant, CancellationToken ct)
    {
        var verified = await signIn.VerifyPasswordAsync(request.Email, request.Password, ct);
        if (verified.IsFailure)
        {
            return verified.Error.ToProblem();
        }

        var user = verified.Value;
        var membership = await signIn.ResolveMembershipAsync(user, request.TenantId ?? tenant.TenantId, ct);
        if (membership.IsFailure)
        {
            return membership.Error.ToProblem();
        }

        if (user.TwoFactorEnabled && membership.Value is { } m)
        {
            return Results.Ok(new LoginResponse(true, tokens.CreateTwoFactorChallenge(user, m.TenantId), null));
        }

        var issued = await signIn.IssueAsync(user, membership.Value, request.ClientType, request.DeviceName, ct);
        return Results.Ok(new LoginResponse(false, null, issued));
    }

    private static async Task<IResult> LoginTwoFactor(
        TwoFactorLoginRequest request, SignInService signIn, TokenService tokens, TotpService totp, IdentityDbContext db, CancellationToken ct)
    {
        var challenge = await tokens.ValidateTwoFactorChallengeAsync(request.TwoFactorToken);
        var user = challenge is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == challenge.Value.UserId, ct);
        if (user is null || user.SecurityStamp != challenge!.Value.Stamp || user.TwoFactorSecret is null)
        {
            return AuthErrors.InvalidTwoFactor.ToProblem();
        }

        var ok = request.Code is { Length: > 0 } code
            ? totp.Verify(user.TwoFactorSecret, code)
            : request.RecoveryCode is { Length: > 0 } recovery && user.RedeemRecoveryCode(SecretHasher.Hash(recovery.Trim().ToLowerInvariant()));

        if (!ok)
        {
            return AuthErrors.InvalidTwoFactor.ToProblem();
        }

        var membership = await signIn.ResolveMembershipAsync(user, challenge.Value.TenantId, ct);
        if (membership.IsFailure)
        {
            return membership.Error.ToProblem();
        }

        var issued = await signIn.IssueAsync(user, membership.Value, request.ClientType, request.DeviceName, ct);
        return Results.Ok(new LoginResponse(false, null, issued));
    }

    private static async Task<IResult> Refresh(RefreshRequest? request, HttpContext http, SignInService signIn, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var token = request?.RefreshToken ?? http.Request.Cookies[options.Value.RefreshCookieName];
        if (string.IsNullOrEmpty(token))
        {
            return AuthErrors.InvalidRefreshToken.ToProblem();
        }

        var result = await signIn.RefreshAsync(token, ct);
        if (result.IsFailure && result.Error == AuthErrors.InvalidRefreshToken)
        {
            signIn.ClearRefreshCookie();
        }

        return result.ToHttp();
    }

    private static async Task<IResult> Register(
        RegisterRequest request, ITenantContext tenant, IdentityDbContext db, SignInService signIn,
        IPasswordHasher<User> hasher, UserTokenService userTokens, IEmailSender email, IOptions<AuthOptions> options,
        TimeProvider clock, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return AuthErrors.TenantRequired.ToProblem();
        }

        var normalized = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
        {
            return AuthErrors.EmailTaken.ToProblem();
        }

        var now = clock.GetUtcNow();
        var user = User.Create(normalized, request.FirstName, request.LastName, request.PhoneNumber);
        user.SetPassword(hasher.HashPassword(user, request.Password), now);
        db.Users.Add(user);

        var memberRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == SystemRoles.Member, ct);
        var membership = TenantMembership.Create(tenantId, user.Id, MembershipStatus.Active, invitedBy: null, now);
        if (memberRole is not null)
        {
            membership.AddRole(memberRole.Id);
        }

        membership.AnnounceAccountCreated(user);
        db.Memberships.Add(membership);

        var tokens = await signIn.IssueAsync(user, membership, request.ClientType, request.DeviceName, ct);

        var confirmToken = userTokens.Create(user, UserTokenPurpose.ConfirmEmail, TimeSpan.FromDays(3));
        await email.SendAsync(EmailTemplates.ConfirmEmail(user, options.Value.AppBaseUrl, confirmToken), ct);

        return Results.Created("/api/v1/me", tokens);
    }

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest request, IdentityDbContext db, UserTokenService userTokens, IEmailSender email,
        IOptions<AuthOptions> options, CancellationToken ct)
    {
        var normalized = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized && u.Status == UserStatus.Active, ct);
        if (user is not null)
        {
            var token = userTokens.Create(user, UserTokenPurpose.ResetPassword, TimeSpan.FromHours(1));
            await email.SendAsync(EmailTemplates.ResetPassword(user, options.Value.AppBaseUrl, token), ct);
        }

        // Identical response whether or not the account exists (no user enumeration).
        return Results.Accepted();
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest request, IdentityDbContext db, UserTokenService userTokens, SignInService signIn,
        IPasswordHasher<User> hasher, TimeProvider clock, CancellationToken ct)
    {
        var normalized = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        var payload = userTokens.Read(request.Token, UserTokenPurpose.ResetPassword);
        if (user is null || payload is null || payload.Value.UserId != user.Id || payload.Value.Stamp != user.SecurityStamp)
        {
            return AuthErrors.InvalidToken.ToProblem();
        }

        user.SetPassword(hasher.HashPassword(user, request.NewPassword), clock.GetUtcNow());
        user.ConfirmEmail(); // Receiving the email proves ownership.
        await signIn.RevokeAllSessionsAsync(user.Id, "password_reset", exceptSessionId: null, ct);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmEmail(ConfirmEmailRequest request, IdentityDbContext db, UserTokenService userTokens, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        var payload = userTokens.Read(request.Token, UserTokenPurpose.ConfirmEmail);
        if (user is null || payload is null || payload.Value.UserId != user.Id || payload.Value.Stamp != user.SecurityStamp)
        {
            return AuthErrors.InvalidToken.ToProblem();
        }

        user.ConfirmEmail();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AcceptInvitation(
        AcceptInvitationRequest request, IdentityDbContext db, UserTokenService userTokens, SignInService signIn,
        IPasswordHasher<User> hasher, TimeProvider clock, CancellationToken ct)
    {
        var normalized = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        var payload = userTokens.Read(request.Token, UserTokenPurpose.AcceptInvitation);
        if (user is null || payload is null || payload.Value.UserId != user.Id || payload.Value.Stamp != user.SecurityStamp)
        {
            return AuthErrors.InvalidToken.ToProblem();
        }

        var now = clock.GetUtcNow();
        user.SetPassword(hasher.HashPassword(user, request.Password), now);
        user.ConfirmEmail();

        var invited = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Invited)
            .ToListAsync(ct);
        invited.ForEach(m => m.Activate(now));
        await db.SaveChangesAsync(ct);

        var membership = await signIn.ResolveMembershipAsync(user, invited.FirstOrDefault()?.TenantId, ct);
        if (membership.IsFailure)
        {
            return membership.Error.ToProblem();
        }

        return Results.Ok(await signIn.IssueAsync(user, membership.Value, request.ClientType, null, ct));
    }

    private static async Task<IResult> Logout(ICurrentUser currentUser, IdentityDbContext db, SignInService signIn, TimeProvider clock, CancellationToken ct)
    {
        if (currentUser.SessionId is { } sessionId)
        {
            var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == currentUser.UserId, ct);
            session?.Revoke(clock.GetUtcNow(), "logout");
            await db.SaveChangesAsync(ct);
        }

        signIn.ClearRefreshCookie();
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request, ICurrentUser currentUser, IdentityDbContext db, SignInService signIn,
        IPasswordHasher<User> hasher, TimeProvider clock, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        if (user.PasswordHash is null ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return Error.Validation("auth.wrong_password", "The current password is incorrect.").ToProblem();
        }

        user.SetPassword(hasher.HashPassword(user, request.NewPassword), clock.GetUtcNow());
        await signIn.RevokeAllSessionsAsync(user.Id, "password_changed", exceptSessionId: currentUser.SessionId, ct);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SwitchTenant(
        SwitchTenantRequest request, ICurrentUser currentUser, IdentityDbContext db, SignInService signIn, CancellationToken ct)
    {
        var user = await db.Users.FirstAsync(u => u.Id == currentUser.RequiredUserId, ct);
        var membership = await signIn.ResolveMembershipAsync(user, request.TenantId, ct);
        if (membership.IsFailure)
        {
            return membership.Error.ToProblem();
        }

        return Results.Ok(await signIn.IssueAsync(user, membership.Value, request.ClientType, null, ct));
    }
}
