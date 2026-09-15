using Ged.Adapters.Persistence.Outbox;
using Ged.Adapters.Persistence.Repositories;
using Ged.Core.Ports;
using Ged.Domain.Blobs;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
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
}
