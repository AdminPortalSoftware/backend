using Platform.SharedKernel.Domain;

namespace Platform.Modules.Tenancy.Domain;

public enum BranchStatus
{
    Active,
    Inactive,
}

/// <summary>
/// A physical location of the organisation (campus / parish / assembly). Most records
/// (people, events, donations) may optionally be attributed to a branch for reporting.
/// </summary>
public sealed class Branch : TenantAggregateRoot
{
    private Branch() { }

    public string Name { get; private set; } = null!;

    /// <summary>Short unique code used in reports and member numbers, e.g. "HQ", "LEK".</summary>
    public string Code { get; private set; } = null!;

    public bool IsHeadquarters { get; private set; }
    public BranchStatus Status { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? TimeZone { get; private set; }
    public Address Address { get; private set; } = Address.Empty;

    /// <summary>Lead pastor / manager — a Person in the People module (reference by id only).</summary>
    public Guid? LeaderPersonId { get; private set; }

    public DateOnly? EstablishedOn { get; private set; }

    public static Branch Create(string name, string code, bool isHeadquarters) => new()
    {
        Name = name,
        Code = code.ToUpperInvariant(),
        IsHeadquarters = isHeadquarters,
        Status = BranchStatus.Active,
    };

    public void Update(
        string name, string code, string? email, string? phone, string? timeZone,
        Address address, Guid? leaderPersonId, DateOnly? establishedOn, BranchStatus status)
    {
        Name = name;
        Code = code.ToUpperInvariant();
        Email = email;
        Phone = phone;
        TimeZone = timeZone;
        Address = address;
        LeaderPersonId = leaderPersonId;
        EstablishedOn = establishedOn;
        Status = status;
    }

    public void SetHeadquarters(bool value) => IsHeadquarters = value;
}
