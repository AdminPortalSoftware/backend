using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

public enum UserStatus
{
    /// <summary>Invited; has not set a password yet.</summary>
    Pending,
    Active,
    Disabled,
}

/// <summary>
/// A global login identity. One person has one account across all tenants; access to a
/// tenant is granted through a <see cref="TenantMembership"/>.
/// </summary>
public sealed class User : AuditableAggregateRoot
{
    private User() { }

    public string Email { get; private set; } = null!;
    public bool EmailConfirmed { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string? PhoneNumber { get; private set; }
    public string? AvatarUrl { get; private set; }
    public UserStatus Status { get; private set; }

    /// <summary>SaaS operator staff. Grants the platform console only — never tenant data.</summary>
    public bool IsPlatformAdmin { get; private set; }

    [AuditIgnore] public string? PasswordHash { get; private set; }

    /// <summary>Rotated on credential changes; invalidates outstanding email/reset tokens.</summary>
    [AuditIgnore] public string SecurityStamp { get; private set; } = NewStamp();

    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>TOTP shared secret, encrypted at rest with ASP.NET Data Protection.</summary>
    [AuditIgnore] public string? TwoFactorSecret { get; private set; }

    /// <summary>SHA-256 hashes of unused recovery codes.</summary>
    [AuditIgnore] public List<string> RecoveryCodeHashes { get; private set; } = [];

    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public static User Create(string email, string firstName, string lastName, string? phoneNumber = null) => new()
    {
        Email = email.Trim().ToLowerInvariant(),
        FirstName = firstName.Trim(),
        LastName = lastName.Trim(),
        PhoneNumber = phoneNumber,
        Status = UserStatus.Pending,
    };

    public void UpdateProfile(string firstName, string lastName, string? phoneNumber, string? avatarUrl)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber;
        AvatarUrl = avatarUrl;
    }

    public void SetPassword(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
        SecurityStamp = NewStamp();
        if (Status == UserStatus.Pending)
        {
            Status = UserStatus.Active;
        }
    }

    /// <summary>Transparent re-hash when the hashing algorithm parameters are upgraded.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void ConfirmEmail() => EmailConfirmed = true;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } end && end > now;

    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts)
        {
            LockoutEndsAt = now.Add(lockoutDuration);
            AccessFailedCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void BeginTwoFactorSetup(string protectedSecret) => TwoFactorSecret = protectedSecret;

    public void EnableTwoFactor(IEnumerable<string> recoveryCodeHashes)
    {
        if (TwoFactorSecret is null)
        {
            throw new DomainException("Two-factor setup has not been started.");
        }

        TwoFactorEnabled = true;
        RecoveryCodeHashes = recoveryCodeHashes.ToList();
        SecurityStamp = NewStamp();
    }

    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
        RecoveryCodeHashes = [];
        SecurityStamp = NewStamp();
    }

    public bool RedeemRecoveryCode(string codeHash) => RecoveryCodeHashes.Remove(codeHash);

    public void Disable()
    {
        Status = UserStatus.Disabled;
        SecurityStamp = NewStamp();
    }

    public void Enable() => Status = PasswordHash is null ? UserStatus.Pending : UserStatus.Active;

    public void GrantPlatformAdmin(bool value) => IsPlatformAdmin = value;

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
