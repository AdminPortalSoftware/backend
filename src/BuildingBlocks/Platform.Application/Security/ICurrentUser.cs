namespace Platform.Application.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }

    /// <summary>The user's membership in the current tenant.</summary>
    Guid? MembershipId { get; }

    /// <summary>Refresh-token session backing the current access token.</summary>
    Guid? SessionId { get; }

    string? Email { get; }
    bool IsPlatformAdmin { get; }

    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");
}
