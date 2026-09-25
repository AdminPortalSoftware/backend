using Platform.Application.Security;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

/// <summary>A named, tenant-specific set of permissions from the <see cref="Permissions"/> catalogue.</summary>
public sealed class Role : TenantAggregateRoot
{
    private Role() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>Provisioned automatically; cannot be deleted or renamed.</summary>
    public bool IsSystem { get; private set; }

    public List<string> Permissions { get; private set; } = [];

    public static Role Create(string name, string? description, IEnumerable<string> permissions, bool isSystem = false)
    {
        var role = new Role { Name = name, Description = description, IsSystem = isSystem };
        role.SetPermissions(permissions);
        return role;
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsSystem && !string.Equals(name, Name, StringComparison.Ordinal))
        {
            throw new DomainException("System roles cannot be renamed.");
        }

        if (IsSystem && Name == SystemRoles.Owner)
        {
            throw new DomainException("The Owner role cannot be modified.");
        }

        Name = name;
        Description = description;
        SetPermissions(permissions);
    }

    private void SetPermissions(IEnumerable<string> permissions)
    {
        var list = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unknown = list.Where(p => !Application.Security.Permissions.IsKnown(p)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException($"Unknown permissions: {string.Join(", ", unknown)}");
        }

        Permissions = list;
    }
}
