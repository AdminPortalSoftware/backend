using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Application.Security;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Features;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Identity.Services;

namespace Platform.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public const string SmartScheme = "Smart";

    public string Name => "Identity";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>(configuration, IdentityDbContext.SchemaName);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDataProtection();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<TokenService>();
        services.AddScoped<SignInService>();
        services.AddScoped<TotpService>();
        services.AddScoped<UserTokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<TenantProvisioning>();
        services.AddHandlersAndValidators(typeof(IdentityModule).Assembly);

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
            ?? throw new InvalidOperationException("Auth configuration is missing.");

        // "Smart" scheme: API key when the X-Api-Key header is present, JWT bearer otherwise.
        services.AddAuthentication(SmartScheme)
            .AddPolicyScheme(SmartScheme, SmartScheme, o => o.ForwardDefaultSelector = ctx =>
                ctx.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                    ? ApiKeyAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = TokenService.ValidationParameters(auth);
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .AddPolicy("PlatformAdmin", p => p.RequireAuthenticatedUser().RequireClaim(PlatformClaims.PlatformAdmin, "true"));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthEndpoints.Map(endpoints);
        MeEndpoints.Map(endpoints);
        AccessManagementEndpoints.Map(endpoints);
    }
}
