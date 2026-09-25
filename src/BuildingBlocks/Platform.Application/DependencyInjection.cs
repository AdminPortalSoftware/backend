using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Messaging;

namespace Platform.Application;

public static class DependencyInjection
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IEventHandler<>),
    ];

    /// <summary>Registers every handler and validator declared in a module assembly.</summary>
    public static IServiceCollection AddHandlersAndValidators(this IServiceCollection services, Assembly assembly)
    {
        var types = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in types)
        {
            foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && HandlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(iface, type);
            }
        }

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }
}
