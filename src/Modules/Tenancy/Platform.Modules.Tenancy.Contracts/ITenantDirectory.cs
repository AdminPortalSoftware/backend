namespace Platform.Modules.Tenancy.Contracts;

public sealed record TenantSummary(Guid Id, string Slug, string Name, string Status, string DefaultCurrency, string TimeZone, string DefaultLocale);

/// <summary>Read-only public API of the Tenancy module for other modules (cached).</summary>
public interface ITenantDirectory
{
    Task<TenantSummary?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
}
