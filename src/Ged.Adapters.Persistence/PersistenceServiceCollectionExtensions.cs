using Ged.Adapters.Persistence.Outbox;
using Ged.Adapters.Persistence.Repositories;
using Ged.Domain.Blobs;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using MicroKit.Core;
using MicroKit.Persistence.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ged.Adapters.Persistence;

/// <summary>Registers the provider-agnostic half of the persistence adapter.</summary>
/// <remarks>
/// Called by <c>AddGedPostgreSql</c> or <c>AddGedSqlServer</c> after they have registered the
/// engine-specific pieces. Everything here is identical on both engines, which is the point: the
/// differences are confined to <c>IPersistenceProvider</c> and to the migration scripts.
/// </remarks>
public static class PersistenceServiceCollectionExtensions
{

    /// <summary>Adds the context consumers, repositories and outbox plumbing.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedPersistenceCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<OutboxInterceptor>();

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IFolderRepository, EfFolderRepository>();
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IBlobRepository, EfBlobRepository>();

        services.AddScoped<OutboxReader>();

        return services;
    }

    ///// <summary>Adds the write-side context, repositories, outbox and read-side connections.</summary>
    ///// <param name="services">The service collection.</param>
    ///// <param name="connectionString">The PostgreSQL connection string.</param>
    ///// <returns>The same service collection, for chaining.</returns>
    ///// <remarks>
    ///// <para>
    ///// The schema is applied by <c>Ged.Migrations</c> before the application starts. Nothing here
    ///// creates or alters it: an application that migrates on startup races with its own replicas
    ///// during a rolling deploy.
    ///// </para>
    ///// <para>
    ///// Only the interfaces are registered. The implementations are <see langword="internal"/>, so a
    ///// caller cannot reach past the contract to the EF types behind it.
    ///// </para>
    ///// </remarks>
    //public static IServiceCollection AddGedPersistence(
    //    this IServiceCollection services, string connectionString)
    //{
    //    ArgumentNullException.ThrowIfNull(services);
    //    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

    //    services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());

    //    services.AddSingleton(TimeProvider.System);
    //    services.AddSingleton<IClock, SystemClock>();
    //    services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

    //    services.AddSingleton<OutboxInterceptor>();

    //    services.AddDbContext<GedDbContext>((provider, options) =>
    //    {
    //        options.UseNpgsql(
    //            provider.GetRequiredService<NpgsqlDataSource>(),
    //            npgsql => npgsql.EnableRetryOnFailure());

    //        options.AddInterceptors(provider.GetRequiredService<OutboxInterceptor>());
    //    });

    //    services.AddScoped<IUnitOfWork, EfUnitOfWork>();
    //    services.AddScoped<IFolderRepository, EfFolderRepository>();
    //    services.AddScoped<IDocumentRepository, EfDocumentRepository>();
    //    services.AddScoped<IBlobRepository, EfBlobRepository>();

    //    services.AddScoped<OutboxReader>();

    //    return services;
    //}
}
