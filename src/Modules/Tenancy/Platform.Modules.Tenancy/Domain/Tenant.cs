using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Tenancy.Domain;

public enum TenantStatus
{
    Trial,
    Active,
    Suspended,
    Cancelled,
}

/// <summary>
/// An organisation using the platform (a church today; any business in the SaaS future).
/// The tenant registry is global — it is the one aggregate that is not itself tenant-scoped.
/// </summary>
public sealed class Tenant : AuditableAggregateRoot
{
    private readonly List<TenantDomain> _domains = [];

    private Tenant() { }

    public string Slug { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? LegalName { get; private set; }
    public TenantStatus Status { get; private set; }

    /// <summary>Kind of organisation (e.g. "church") — drives defaults and enabled modules later.</summary>
    public string Kind { get; private set; } = "church";

    public string? PlanCode { get; private set; }
    public DateTimeOffset? TrialEndsAt { get; private set; }

    public string TimeZone { get; private set; } = "UTC";
    public string DefaultCurrency { get; private set; } = "USD";
    public string DefaultLocale { get; private set; } = "en";
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public string? LogoUrl { get; private set; }
    public Address Address { get; private set; } = Address.Empty;

    public IReadOnlyCollection<TenantDomain> Domains => _domains.AsReadOnly();

    public static Tenant Create(
        string slug, string name, string kind, string timeZone, string currency, string locale,
        string ownerEmail, string ownerFirstName, string ownerLastName)
    {
        var tenant = new Tenant
        {
            Slug = slug.ToLowerInvariant(),
            Name = name,
            Kind = kind,
            Status = TenantStatus.Active,
            TimeZone = timeZone,
            DefaultCurrency = currency.ToUpperInvariant(),
            DefaultLocale = locale,
            ContactEmail = ownerEmail,
        };

        tenant.Raise(new TenantCreatedIntegrationEvent(
            tenant.Id, tenant.Slug, tenant.Name, tenant.DefaultCurrency, ownerEmail, ownerFirstName, ownerLastName));
        return tenant;
    }

    public void UpdateProfile(
        string name, string? legalName, string timeZone, string currency, string locale,
        string? contactEmail, string? contactPhone, string? websiteUrl, string? logoUrl, Address address)
    {
        Name = name;
        LegalName = legalName;
        TimeZone = timeZone;
        DefaultCurrency = currency.ToUpperInvariant();
        DefaultLocale = locale;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        WebsiteUrl = websiteUrl;
        LogoUrl = logoUrl;
        Address = address;
    }

    public void ChangeStatus(TenantStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        Raise(new TenantStatusChangedIntegrationEvent(Id, status.ToString()));
    }

    public TenantDomain AddDomain(string host, bool isPrimary)
    {
        host = host.Trim().ToLowerInvariant();
        if (_domains.Any(d => d.Host == host))
        {
            throw new DomainException($"Domain '{host}' is already registered for this tenant.");
        }

        if (isPrimary)
        {
            _domains.ForEach(d => d.IsPrimary = false);
        }

        var domain = new TenantDomain(Id, host, isPrimary || _domains.Count == 0);
        _domains.Add(domain);
        return domain;
    }

    public void RemoveDomain(Guid domainId) => _domains.RemoveAll(d => d.Id == domainId);
}

/// <summary>Custom domain that serves the tenant's public website.</summary>
public sealed class TenantDomain : Entity
{
    private TenantDomain() { }

    internal TenantDomain(Guid tenantId, string host, bool isPrimary)
    {
        TenantId = tenantId;
        Host = host;
        IsPrimary = isPrimary;
        VerificationToken = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    }

    public Guid TenantId { get; private set; }
    public string Host { get; private set; } = null!;
    public bool IsPrimary { get; internal set; }

    /// <summary>Value the tenant publishes as a DNS TXT record to prove ownership.</summary>
    public string VerificationToken { get; private set; } = null!;

    public DateTimeOffset? VerifiedAt { get; private set; }

    public void MarkVerified(DateTimeOffset at) => VerifiedAt = at;
}
