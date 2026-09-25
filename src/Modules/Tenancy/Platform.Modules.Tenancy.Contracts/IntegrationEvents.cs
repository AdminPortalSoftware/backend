using Platform.SharedKernel.Domain;

namespace Platform.Modules.Tenancy.Contracts;

/// <summary>
/// A new organisation was created. Identity provisions default roles and invites the owner;
/// other modules may seed defaults (e.g. Giving creates the standard funds).
/// </summary>
public sealed record TenantCreatedIntegrationEvent(
    Guid TenantId,
    string Slug,
    string Name,
    string DefaultCurrency,
    string OwnerEmail,
    string OwnerFirstName,
    string OwnerLastName) : IntegrationEvent(TenantId);

public sealed record TenantStatusChangedIntegrationEvent(Guid TenantId, string Status) : IntegrationEvent(TenantId);
