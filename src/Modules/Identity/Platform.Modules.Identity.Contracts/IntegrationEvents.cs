using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Contracts;

/// <summary>
/// A user self-registered (website / mobile app) or was invited into a tenant.
/// The People module links or creates the matching person record.
/// </summary>
public sealed record MemberAccountCreatedIntegrationEvent(
    Guid TenantId,
    Guid UserId,
    Guid MembershipId,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber) : IntegrationEvent(TenantId);
