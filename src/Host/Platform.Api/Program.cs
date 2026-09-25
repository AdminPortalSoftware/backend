using Platform.Api;
using Platform.Api.Configuration;
using Platform.Api.Seeding;
using Platform.Infrastructure;
using Platform.Web;
using Platform.Web.Middleware;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services
    .AddPlatformInfrastructure(builder.Configuration)
    .AddPlatformWeb()
    .AddApiPlatform(builder.Configuration, builder.Environment);

foreach (var module in Modules.All)
{
    module.Register(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (diag, http) =>
{
    diag.Set("TenantId", http.User.FindFirst("tid")?.Value ?? http.Request.Headers[TenantResolutionMiddleware.TenantHeader].ToString());
    diag.Set("UserId", http.User.FindFirst("sub")?.Value);
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors(ServiceCollectionExtensions.CorsPolicy);
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/docs", o => o.WithTitle("Platform API"));

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

foreach (var module in Modules.All)
{
    module.MapEndpoints(app);
}

await DatabaseInitializer.InitialiseAsync(app);
await app.RunAsync();

/// <summary>Entry point marker for integration tests (WebApplicationFactory).</summary>
public partial class Program;
