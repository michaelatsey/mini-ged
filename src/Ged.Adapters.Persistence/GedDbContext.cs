using Ged.Adapters.Persistence.Providers;
using Ged.Adapters.Persistence.Outbox;
using Ged.Domain.Blobs;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using System.Reflection;

namespace Ged.Adapters.Persistence;

/// <summary>
/// The write-side context: aggregates, their invariants, and the outbox that travels with them.
/// </summary>
/// <param name="options">The context options, carrying the engine-specific provider.</param>
/// <param name="provider">The dialect and conventions of the engine in use.</param>
/// <remarks>
/// <para>
/// This context maps onto a schema it does not own. DbUp applies the schema; EF Core never
/// generates it, has no migrations folder, and <c>Database.Migrate</c> is never called. Two tools
/// able to change the same schema will eventually disagree, and the disagreement surfaces as a
/// startup failure in an environment nobody was watching.
/// </para>
/// <para>
/// Reads do not come through here. Query handlers open a connection and write the SQL their screen
/// needs: change tracking exists to protect invariants during a mutation, and it is pure cost on a
/// projection that will never be saved.
/// </para>
/// </remarks>
public sealed class GedDbContext(
    DbContextOptions<GedDbContext> options,
    IPersistenceProvider provider) : DbContext(options)
{
    /// <summary>Gets the folder aggregates.</summary>
    public DbSet<Folder> Folders => Set<Folder>();

    /// <summary>Gets the document aggregates.</summary>
    public DbSet<Document> Documents => Set<Document>();

    /// <summary>Gets the blob aggregates.</summary>
    public DbSet<Blob> Blobs => Set<Blob>();

    /// <summary>Gets the pending and delivered outbox messages.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Gets the dialect and conventions of the engine in use.</summary>
    internal IPersistenceProvider Provider => provider;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // The provider travels through the model builder so each configuration can ask for the
        // engine-specific pieces without every configuration taking a constructor argument.
        modelBuilder.ApplyConfigurationsFromAssembly(
            Assembly.GetExecutingAssembly(),
            type => type.GetConstructor([typeof(IPersistenceProvider)]) is { } ctor
                ? ctor.Invoke([provider])
                : Activator.CreateInstance(type));

        base.OnModelCreating(modelBuilder);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // The domain stores instants, never local times. Pinning the store type here means a
        // misconfigured server cannot silently reinterpret a retention window.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveColumnType(provider.InstantColumnType);

        base.ConfigureConventions(configurationBuilder);
    }
}
