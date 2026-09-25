using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Application.Abstractions;
using Platform.Application.Messaging;
using Platform.Application.Security;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;
using Platform.Modules.Tenancy.Contracts;

namespace Platform.Modules.Identity.Features;

/// <summary>
/// Provisions a new tenant: system roles and the owner's membership. Idempotent — safe for
/// at-least-once delivery and for being invoked directly by the seeder.
/// </summary>
public sealed class TenantProvisioning(
    IdentityDbContext db,
    UserTokenService userTokens,
    IEmailSender email,
    IOptions<AuthOptions> options,
    TimeProvider clock) : IEventHandler<TenantCreatedIntegrationEvent>
{
    public async Task Handle(TenantCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var roles = await EnsureSystemRolesAsync(e.TenantId, cancellationToken);

        var normalized = e.OwnerEmail.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        if (user is null)
        {
            user = User.Create(normalized, e.OwnerFirstName, e.OwnerLastName);
            db.Users.Add(user);
        }

        var membership = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(m => m.TenantId == e.TenantId && m.UserId == user.Id, cancellationToken);

        var sendInvite = false;
        if (membership is null)
        {
            var hasPassword = user.PasswordHash is not null;
            membership = TenantMembership.Create(e.TenantId, user.Id,
                hasPassword ? MembershipStatus.Active : MembershipStatus.Invited, invitedBy: null, clock.GetUtcNow());
            membership.AnnounceAccountCreated(user);
            db.Memberships.Add(membership);
            sendInvite = !hasPassword;
        }

        membership.AddRole(roles[SystemRoles.Owner].Id);
        await db.SaveChangesAsync(cancellationToken);

        if (sendInvite)
        {
            var token = userTokens.Create(user, UserTokenPurpose.AcceptInvitation, TimeSpan.FromDays(7));
            await email.SendAsync(EmailTemplates.Invitation(user, e.Name, options.Value.AppBaseUrl, token), cancellationToken);
        }
    }

    /// <summary>Creates any missing system roles; existing roles keep tenant customisations.</summary>
    public async Task<Dictionary<string, Role>> EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Roles.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(r => r.TenantId == tenantId && r.IsSystem)
            .ToDictionaryAsync(r => r.Name, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var (name, (description, permissions)) in SystemRoles.Defaults)
        {
            if (existing.ContainsKey(name))
            {
                continue;
            }

            var role = Role.Create(name, description, permissions, isSystem: true);
            role.AssignTenant(tenantId);
            db.Roles.Add(role);
            existing[name] = role;
        }

        return existing;
    }
}
