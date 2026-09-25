namespace Platform.Application.Security;

/// <summary>Resolves effective permissions for a user within a tenant (cached).</summary>
public interface IPermissionService
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);

    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken);
}
