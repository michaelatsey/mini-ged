using Ged.Adapters.Persistence.Outbox;
using Ged.Adapters.Persistence.Providers;
using Ged.Core.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ged.Adapters.Persistence.SqlServer;

/// <summary>Registers the SQL Server persistence adapter.</summary>
public static class SqlServerServiceCollectionExtensions
{
    /// <summary>Adds the write side, the read-side connections and the outbox, on SQL Server.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedSqlServer(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<IDbConnectionFactory>(
            _ => new SqlServerConnectionFactory(connectionString));

        services.AddSingleton<IPersistenceProvider, SqlServerPersistenceProvider>();

        services.AddGedPersistenceCore();

        services.AddDbContext<GedDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            options.AddInterceptors(provider.GetRequiredService<OutboxInterceptor>());
        });

        return services;
    }
}
