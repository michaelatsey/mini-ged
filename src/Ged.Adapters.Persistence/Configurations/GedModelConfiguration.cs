using Ged.Adapters.Persistence.Providers;

namespace Ged.Adapters.Persistence.Configurations;

internal static class GedModelConfiguration
{
    public static void Apply(
        ModelBuilder modelBuilder,
        IPersistenceProvider provider)
    {
        modelBuilder.ApplyConfiguration(
            new FolderConfiguration(provider));

        modelBuilder.ApplyConfiguration(
            new DocumentConfiguration(provider));

        modelBuilder.ApplyConfiguration(
            new BlobConfiguration(provider));

        modelBuilder.ApplyConfiguration(
            new OutboxMessageConfiguration(provider));
    }
}
