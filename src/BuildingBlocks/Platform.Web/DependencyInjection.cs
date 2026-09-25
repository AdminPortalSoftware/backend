using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Security;
using Platform.Infrastructure.Auditing;
using Platform.Web.Errors;
using Platform.Web.Security;

namespace Platform.Web;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformWeb(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IRequestInfo, HttpRequestInfo>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
