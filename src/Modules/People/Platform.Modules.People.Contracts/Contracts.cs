using Platform.SharedKernel.Domain;

namespace Platform.Modules.People.Contracts;

/// <summary>A person profile was linked to a login account (Identity stores the link on the membership).</summary>
public sealed record PersonLinkedToAccountIntegrationEvent(Guid TenantId, Guid PersonId, Guid UserId, Guid MembershipId)
    : IntegrationEvent(TenantId);

public sealed record PersonCreatedIntegrationEvent(Guid TenantId, Guid PersonId, string FullName, string MembershipStatus, Guid? BranchId)
    : IntegrationEvent(TenantId);

public sealed record PersonMembershipStatusChangedIntegrationEvent(Guid TenantId, Guid PersonId, string From, string To)
    : IntegrationEvent(TenantId);

public sealed record PersonSummary(Guid Id, string MemberNumber, string FullName, string? Email, string? PhoneNumber, string? PhotoUrl, Guid? BranchId);

/// <summary>Read-only public API of the People module (e.g. donor names in Giving, rosters in Groups).</summary>
public interface IPeopleDirectory
{
    Task<IReadOnlyDictionary<Guid, PersonSummary>> GetSummariesAsync(IEnumerable<Guid> personIds, CancellationToken cancellationToken);

    Task<Guid?> FindPersonIdByUserAsync(Guid userId, CancellationToken cancellationToken);
}
