using Ged.Adapters.Persistence.Configurations;
using Ged.Adapters.Persistence.Outbox;
using Ged.Adapters.Persistence.Providers;
using Ged.Domain.Blobs;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Ged.Adapters.Persistence;

/// <summary>
/// The write-side persistence context for aggregates and the transactional outbox.
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
[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
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
        base.OnModelCreating(modelBuilder);

        GedModelConfiguration.Apply(modelBuilder, provider);

    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The domain stores instants, never local times. Pinning the store type here means a
        // misconfigured server cannot silently reinterpret a retention window.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveColumnType(provider.InstantColumnType);

        base.ConfigureConventions(configurationBuilder);
    }
}
