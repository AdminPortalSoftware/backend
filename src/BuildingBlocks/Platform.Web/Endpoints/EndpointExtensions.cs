using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Platform.Web.Endpoints;

public static class EndpointExtensions
{
    public const string ApiPrefix = "/api/v1";

    /// <summary>Creates the versioned route group for a module's admin/member API.</summary>
    public static RouteGroupBuilder MapModuleGroup(this IEndpointRouteBuilder endpoints, string path, string tag) =>
        endpoints.MapGroup($"{ApiPrefix}/{path.Trim('/')}")
            .WithTags(tag)
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    /// <summary>
    /// Creates the versioned route group for the anonymous public API consumed by the website
    /// and mobile app. The tenant is resolved from the X-Tenant header or the request host.
    /// </summary>
    public static RouteGroupBuilder MapPublicGroup(this IEndpointRouteBuilder endpoints, string path, string tag) =>
        endpoints.MapGroup($"{ApiPrefix}/public/{path.Trim('/')}")
            .WithTags(tag)
            .AllowAnonymous()
            .RequireRateLimiting("public");
}
