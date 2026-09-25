using Platform.Modules.Identity.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

public enum MembershipStatus
{
    Invited,
    Active,
    Suspended,
}

/// <summary>
/// Grants a <see cref="User"/> access to one tenant with a set of roles. A user can belong
/// to many tenants (e.g. a pastor overseeing several churches in the SaaS future).
/// </summary>
public sealed class TenantMembership : TenantAggregateRoot
{
    private readonly List<MembershipRole> _roles = [];

    private TenantMembership() { }

    public Guid UserId { get; private set; }
    public MembershipStatus Status { get; private set; }

    /// <summary>The person profile in the People module (reference by id only).</summary>
    public Guid? PersonId { get; private set; }

    /// <summary>Branch the user works in by default (admin UI filter), optional.</summary>
    public Guid? DefaultBranchId { get; private set; }

    public DateTimeOffset? JoinedAt { get; private set; }
    public Guid? InvitedBy { get; private set; }

    public IReadOnlyCollection<MembershipRole> Roles => _roles.AsReadOnly();

    public static TenantMembership Create(Guid tenantId, Guid userId, MembershipStatus status, Guid? invitedBy, DateTimeOffset now)
    {
        var membership = new TenantMembership
        {
            UserId = userId,
            Status = status,
            InvitedBy = invitedBy,
            JoinedAt = status == MembershipStatus.Active ? now : null,
        };
        membership.AssignTenant(tenantId);
        return membership;
    }

    public void Activate(DateTimeOffset now)
    {
        Status = MembershipStatus.Active;
        JoinedAt ??= now;
    }

    public void Suspend() => Status = MembershipStatus.Suspended;

    public void LinkPerson(Guid personId) => PersonId = personId;

    /// <summary>Tells other modules (People) that an account now exists for this tenant.</summary>
    public void AnnounceAccountCreated(User user) =>
        Raise(new MemberAccountCreatedIntegrationEvent(TenantId, UserId, Id, user.Email, user.FirstName, user.LastName, user.PhoneNumber));

    public void SetDefaultBranch(Guid? branchId) => DefaultBranchId = branchId;

    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        var desired = roleIds.ToHashSet();
        _roles.RemoveAll(r => !desired.Contains(r.RoleId));
        foreach (var roleId in desired.Where(id => _roles.All(r => r.RoleId != id)))
        {
            _roles.Add(new MembershipRole(Id, roleId));
        }
    }

    public void AddRole(Guid roleId)
    {
        if (_roles.All(r => r.RoleId != roleId))
        {
            _roles.Add(new MembershipRole(Id, roleId));
        }
    }
}

public sealed class MembershipRole
{
    private MembershipRole() { }

    internal MembershipRole(Guid membershipId, Guid roleId)
    {
        MembershipId = membershipId;
        RoleId = roleId;
    }

    public Guid MembershipId { get; private set; }
    public Guid RoleId { get; private set; }
}
