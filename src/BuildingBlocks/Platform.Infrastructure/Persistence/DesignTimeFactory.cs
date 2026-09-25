using Microsoft.EntityFrameworkCore;
using Platform.Application.Tenancy;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Base for module <c>IDesignTimeDbContextFactory</c>s so `dotnet ef` can build a context
/// without the host. Uses <c>ConnectionStrings__Database</c> or a local default.
/// </summary>
public abstract class DesignTimeFactory<TContext> where TContext : ModuleDbContext
{
    protected abstract string Schema { get; }

    protected abstract TContext Create(DbContextOptions<TContext> options, ITenantContext tenantContext);

    public TContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=platform;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<TContext>();
        PersistenceExtensions.ConfigureNpgsql(builder, connectionString, Schema, typeof(TContext).Assembly.GetName().Name!);
        return Create(builder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
    }
}
