using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Platform.Application.Security;
using Platform.Infrastructure.Auditing;

namespace Platform.Web.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid? UserId => ParseGuid(PlatformClaims.UserId);
    public Guid? MembershipId => ParseGuid(PlatformClaims.MembershipId);
    public Guid? SessionId => ParseGuid(PlatformClaims.SessionId);
    public string? Email => Principal?.FindFirstValue(PlatformClaims.Email);
    public bool IsPlatformAdmin => Principal?.FindFirstValue(PlatformClaims.PlatformAdmin) == "true";

    private Guid? ParseGuid(string claim) =>
        Guid.TryParse(Principal?.FindFirstValue(claim), out var value) ? value : null;
}

internal sealed class HttpRequestInfo(IHttpContextAccessor accessor) : IRequestInfo
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 512)] : null;
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
