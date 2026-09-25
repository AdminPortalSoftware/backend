using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Auditing;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Identity.Features;

public sealed record StaffUserResponse(
    Guid MembershipId, Guid UserId, string Email, string FirstName, string LastName, string? AvatarUrl,
    string MembershipStatus, string UserStatus, bool TwoFactorEnabled, Guid? PersonId, Guid? DefaultBranchId,
    IReadOnlyList<Guid> RoleIds, DateTimeOffset? LastLoginAt, DateTimeOffset? JoinedAt);

public sealed record InviteUserRequest(string Email, string FirstName, string LastName, IReadOnlyList<Guid> RoleIds, Guid? DefaultBranchId);

public sealed record SetRolesRequest(IReadOnlyList<Guid> RoleIds);

public sealed record RoleResponse(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int MemberCount);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record PermissionGroupResponse(string Module, IReadOnlyList<string> Permissions);

public sealed record ApiKeyResponse(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, DateTimeOffset CreatedAt);

public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

public sealed record CreatedApiKeyResponse(ApiKeyResponse ApiKey, string Key);

public sealed record AuditEntryResponse(Guid Id, Guid? UserId, string Module, string EntityType, string EntityId, string Action,
    string? Changes, string? IpAddress, string? CorrelationId, DateTimeOffset OccurredAt);

internal sealed class InviteUserValidator : AbstractValidator<InviteUserRequest>
{
    public InviteUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoleIds).NotNull();
    }
}

internal sealed class SaveRoleValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleForEach(x => x.Permissions).Must(Permissions.IsKnown).WithMessage("Unknown permission '{PropertyValue}'.");
    }
}

internal sealed class CreateApiKeyValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Scopes).NotEmpty();
        RuleForEach(x => x.Scopes).Must(Permissions.IsKnown).WithMessage("Unknown scope '{PropertyValue}'.");
    }
}

/// <summary>Tenant administration of staff accounts, roles, API keys and the audit trail.</summary>
public static class AccessManagementEndpoints
{
    private static readonly Error MembershipNotFound = Error.NotFound("user.not_found", "The user was not found in this organisation.");
    private static readonly Error RoleNotFound = Error.NotFound("role.not_found", "The role was not found.");
    private static readonly Error RoleNameTaken = Error.Conflict("role.name_taken", "A role with this name already exists.");
    private static readonly Error InvalidRoles = Error.Validation("role.invalid", "One or more roles do not exist.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapModuleGroup("users", "Users & access");
        users.MapGet("/", ListUsers).RequirePermission(Permissions.Identity.UsersRead).WithSummary("List staff and member accounts");
        users.MapPost("/invite", InviteUser).WithValidation<InviteUserRequest>().RequirePermission(Permissions.Identity.UsersManage)
            .WithSummary("Invite a user (or grant an existing account access) with roles");
        users.MapPut("/{membershipId:guid}/roles", SetRoles).RequirePermission(Permissions.Identity.UsersManage).WithSummary("Replace a user's roles");
        users.MapPost("/{membershipId:guid}/suspend", Suspend).RequirePermission(Permissions.Identity.UsersManage).WithSummary("Suspend access and sign out all sessions");
        users.MapPost("/{membershipId:guid}/activate", Activate).RequirePermission(Permissions.Identity.UsersManage).WithSummary("Restore access");

        var roles = endpoints.MapModuleGroup("roles", "Users & access");
        roles.MapGet("/", ListRoles).RequirePermission(Permissions.Identity.RolesRead).WithSummary("List roles");
        roles.MapGet("/permissions", ListPermissions).RequirePermission(Permissions.Identity.RolesRead).WithSummary("Permission catalogue for the role editor");
        roles.MapPost("/", CreateRole).WithValidation<SaveRoleRequest>().RequirePermission(Permissions.Identity.RolesManage).WithSummary("Create a role");
        roles.MapPut("/{id:guid}", UpdateRole).WithValidation<SaveRoleRequest>().RequirePermission(Permissions.Identity.RolesManage).WithSummary("Update a role");
        roles.MapDelete("/{id:guid}", DeleteRole).RequirePermission(Permissions.Identity.RolesManage).WithSummary("Delete a custom role");

        var keys = endpoints.MapModuleGroup("api-keys", "Users & access");
        keys.MapGet("/", ListApiKeys).RequirePermission(Permissions.Identity.ApiKeysManage).WithSummary("List API keys");
        keys.MapPost("/", CreateApiKey).WithValidation<CreateApiKeyRequest>().RequirePermission(Permissions.Identity.ApiKeysManage)
            .WithSummary("Create an API key (the secret is shown once)");
        keys.MapDelete("/{id:guid}", RevokeApiKey).RequirePermission(Permissions.Identity.ApiKeysManage).WithSummary("Revoke an API key");

        endpoints.MapModuleGroup("audit", "Audit trail")
            .MapGet("/", ListAudit).RequirePermission(Permissions.Identity.AuditRead).WithSummary("Search the audit trail");
    }

    private static async Task<IResult> ListUsers([AsParameters] PageRequest page, string? search, Guid? roleId, IdentityDbContext db, CancellationToken ct)
    {
        var query =
            from m in db.Memberships.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            select new { m, u };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.u.Email, pattern) || EF.Functions.ILike(x.u.FirstName + " " + x.u.LastName, pattern));
        }

        if (roleId is { } rid)
        {
            query = query.Where(x => x.m.Roles.Any(r => r.RoleId == rid));
        }

        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(x => x.u.LastName).ThenBy(x => x.u.FirstName)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new StaffUserResponse(
                x.m.Id, x.u.Id, x.u.Email, x.u.FirstName, x.u.LastName, x.u.AvatarUrl, x.m.Status.ToString(), x.u.Status.ToString(),
                x.u.TwoFactorEnabled, x.m.PersonId, x.m.DefaultBranchId, x.m.Roles.Select(r => r.RoleId).ToList(), x.u.LastLoginAt, x.m.JoinedAt))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<StaffUserResponse>(rows, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> InviteUser(
        InviteUserRequest request, ITenantContext tenant, ICurrentUser currentUser, IdentityDbContext db,
        IPermissionService permissions, UserTokenService userTokens, IEmailSender email, IOptions<AuthOptions> options,
        ITenantDirectory tenants, TimeProvider clock, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        if (!await AllRolesExist(db, request.RoleIds, ct))
        {
            return InvalidRoles.ToProblem();
        }

        var now = clock.GetUtcNow();
        var normalized = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        var isNewUser = user is null;
        if (user is null)
        {
            user = User.Create(normalized, request.FirstName, request.LastName);
            db.Users.Add(user);
        }

        if (await db.Memberships.AnyAsync(m => m.UserId == user.Id, ct))
        {
            return Error.Conflict("user.already_member", "This person already has access. Edit their roles instead.").ToProblem();
        }

        var hasPassword = user.PasswordHash is not null;
        var membership = TenantMembership.Create(
            tenantId, user.Id, hasPassword ? MembershipStatus.Active : MembershipStatus.Invited, currentUser.UserId, now);
        membership.SetRoles(request.RoleIds);
        membership.SetDefaultBranch(request.DefaultBranchId);
        if (isNewUser)
        {
            membership.AnnounceAccountCreated(user);
        }

        db.Memberships.Add(membership);
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenantId, ct);

        var organisation = (await tenants.GetAsync(tenantId, ct))?.Name ?? "your organisation";
        var message = hasPassword
            ? EmailTemplates.AddedToOrganisation(user, organisation, options.Value.AppBaseUrl)
            : EmailTemplates.Invitation(user, organisation, options.Value.AppBaseUrl,
                userTokens.Create(user, UserTokenPurpose.AcceptInvitation, TimeSpan.FromDays(7)));
        await email.SendAsync(message, ct);

        return Results.Created($"/api/v1/users/{membership.Id}", new { membershipId = membership.Id, userId = user.Id, status = membership.Status.ToString() });
    }

    private static async Task<IResult> SetRoles(
        Guid membershipId, SetRolesRequest request, ITenantContext tenant, ICurrentUser currentUser, IdentityDbContext db,
        IPermissionService permissions, CancellationToken ct)
    {
        var membership = await db.Memberships.Include(m => m.Roles).FirstOrDefaultAsync(m => m.Id == membershipId, ct);
        if (membership is null)
        {
            return MembershipNotFound.ToProblem();
        }

        if (!await AllRolesExist(db, request.RoleIds, ct))
        {
            return InvalidRoles.ToProblem();
        }

        // Guard against locking the organisation out: at least one Owner must remain.
        var ownerRoleId = await db.Roles.Where(r => r.Name == SystemRoles.Owner && r.IsSystem).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (ownerRoleId is { } owner && membership.Roles.Any(r => r.RoleId == owner) && !request.RoleIds.Contains(owner))
        {
            var otherOwners = await db.Memberships.CountAsync(m => m.Id != membershipId && m.Status == MembershipStatus.Active && m.Roles.Any(r => r.RoleId == owner), ct);
            if (otherOwners == 0)
            {
                return Error.Conflict("role.last_owner", "The organisation must keep at least one Owner.").ToProblem();
            }
        }

        membership.SetRoles(request.RoleIds);
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Suspend(
        Guid membershipId, ITenantContext tenant, ICurrentUser currentUser, IdentityDbContext db, IPermissionService permissions,
        TimeProvider clock, CancellationToken ct)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct);
        if (membership is null)
        {
            return MembershipNotFound.ToProblem();
        }

        if (membership.UserId == currentUser.UserId)
        {
            return Error.Conflict("user.cannot_suspend_self", "You cannot suspend your own access.").ToProblem();
        }

        membership.Suspend();
        var now = clock.GetUtcNow();
        var sessions = await db.Sessions.Where(s => s.MembershipId == membershipId && s.RevokedAt == null).ToListAsync(ct);
        sessions.ForEach(s => s.Revoke(now, "membership_suspended"));
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Activate(Guid membershipId, ITenantContext tenant, IdentityDbContext db, IPermissionService permissions,
        TimeProvider clock, CancellationToken ct)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct);
        if (membership is null)
        {
            return MembershipNotFound.ToProblem();
        }

        membership.Activate(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListRoles(IdentityDbContext db, CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleResponse(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions,
                db.MembershipRoles.Count(mr => mr.RoleId == r.Id)))
            .ToListAsync(ct);
        return Results.Ok(roles);
    }

    private static IResult ListPermissions() =>
        Results.Ok(Permissions.All
            .GroupBy(p => p.Split('.')[0])
            .OrderBy(g => g.Key)
            .Select(g => new PermissionGroupResponse(g.Key, g.Order().ToList())));

    private static async Task<IResult> CreateRole(SaveRoleRequest request, IdentityDbContext db, CancellationToken ct)
    {
        if (await db.Roles.AnyAsync(r => r.Name == request.Name, ct))
        {
            return RoleNameTaken.ToProblem();
        }

        var role = Role.Create(request.Name.Trim(), request.Description, request.Permissions);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/roles/{role.Id}", new RoleResponse(role.Id, role.Name, role.Description, false, role.Permissions, 0));
    }

    private static async Task<IResult> UpdateRole(Guid id, SaveRoleRequest request, ITenantContext tenant, IdentityDbContext db,
        IPermissionService permissions, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return RoleNotFound.ToProblem();
        }

        if (await db.Roles.AnyAsync(r => r.Name == request.Name && r.Id != id, ct))
        {
            return RoleNameTaken.ToProblem();
        }

        role.Update(request.Name.Trim(), request.Description, request.Permissions);
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRole(Guid id, ITenantContext tenant, IdentityDbContext db, IPermissionService permissions, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
        {
            return RoleNotFound.ToProblem();
        }

        if (role.IsSystem)
        {
            return Error.Conflict("role.system", "System roles cannot be deleted.").ToProblem();
        }

        await db.MembershipRoles.Where(mr => mr.RoleId == id).ExecuteDeleteAsync(ct);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await permissions.InvalidateAsync(tenant.RequiredTenantId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListApiKeys(IdentityDbContext db, CancellationToken ct) =>
        Results.Ok(await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyResponse(k.Id, k.Name, k.Prefix, k.Scopes, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.CreatedAt))
            .ToListAsync(ct));

    private static async Task<IResult> CreateApiKey(CreateApiKeyRequest request, IdentityDbContext db, CancellationToken ct)
    {
        var prefix = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
        var key = $"pk_{prefix}_{SecretHasher.NewSecret()}";
        var apiKey = ApiKey.Create(request.Name.Trim(), prefix, SecretHasher.Hash(key), request.Scopes, request.ExpiresAt);
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/api-keys/{apiKey.Id}", new CreatedApiKeyResponse(
            new ApiKeyResponse(apiKey.Id, apiKey.Name, apiKey.Prefix, apiKey.Scopes, apiKey.ExpiresAt, null, null, apiKey.CreatedAt), key));
    }

    private static async Task<IResult> RevokeApiKey(Guid id, IdentityDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (apiKey is null)
        {
            return Error.NotFound("apikey.not_found", "API key not found.").ToProblem();
        }

        apiKey.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAudit(
        [AsParameters] PageRequest page, string? entityType, string? entityId, Guid? userId, DateTimeOffset? from, DateTimeOffset? to,
        ITenantContext tenant, IdentityDbContext db, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var query = db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(a => a.EntityId == entityId);
        if (userId is not null) query = query.Where(a => a.UserId == userId);
        if (from is not null) query = query.Where(a => a.OccurredAt >= from);
        if (to is not null) query = query.Where(a => a.OccurredAt < to);

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(a => a.OccurredAt).Skip(page.Skip).Take(page.SafePageSize)
            .Select(a => new AuditEntryResponse(a.Id, a.UserId, a.Module, a.EntityType, a.EntityId, a.Action.ToString(), a.Changes,
                a.IpAddress, a.CorrelationId, a.OccurredAt))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<AuditEntryResponse>(items, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<bool> AllRolesExist(IdentityDbContext db, IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var distinct = roleIds.Distinct().ToList();
        return await db.Roles.CountAsync(r => distinct.Contains(r.Id), ct) == distinct.Count;
    }
}
