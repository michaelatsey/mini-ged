using Ged.Adapters.Persistence.Outbox;
using Ged.Adapters.Persistence.Providers;
using Ged.Core.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ged.Adapters.Persistence.PostgreSql;

/// <summary>Registers the PostgreSQL persistence adapter.</summary>
public static class PostgreSqlServiceCollectionExtensions
{
    /// <summary>Adds the write side, the read-side connections and the outbox, on PostgreSQL.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="postgresMajorVersion">
    /// The server's major version. Declaring it lets the provider emit features the target actually
    /// has — from version 18 it translates <c>Guid.CreateVersion7()</c> to the native function —
    /// instead of assuming the lowest common denominator.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedPostgreSql(
        this IServiceCollection services,
        string connectionString,
        int postgresMajorVersion = 16)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<IPersistenceProvider, PostgreSqlPersistenceProvider>();

        services.AddGedPersistenceCore();

        services.AddDbContext<GedDbContext>((provider, options) =>
        {
            options.UseNpgsql(
                provider.GetRequiredService<NpgsqlDataSource>(),
                npgsql =>
                {
                    npgsql.SetPostgresVersion(postgresMajorVersion, 0);
                    npgsql.EnableRetryOnFailure();
                });

            options.AddInterceptors(provider.GetRequiredService<OutboxInterceptor>());
        });

        return services;
    }
}
