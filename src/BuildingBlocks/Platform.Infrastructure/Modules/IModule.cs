using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Infrastructure.Modules;

/// <summary>
/// A vertical business capability (People, Giving, …). Modules never reference each other;
/// they communicate through integration events and their *.Contracts assemblies only.
/// Each module could be lifted into its own service without changing its internals.
/// </summary>
public interface IModule
{
    string Name { get; }

    void Register(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

/// <summary>Implemented by a module DbContext registry so the host can migrate every module.</summary>
public interface IModuleDatabase
{
    Type ContextType { get; }
}

internal sealed record ModuleDatabase(Type ContextType) : IModuleDatabase;
