using Ged.Adapters.Persistence.Converters;
using Ged.Domain.Blobs;
using Ged.Domain.Blobs.ValueObjects;

namespace Ged.Adapters.Persistence.Configurations;

internal static class BlobLocationConfiguration
{
    public static void Configure(OwnedNavigationBuilder<Blob, BlobLocation> builder)
    {
        builder.ToTable("blob_location");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(GedValueConverters.BlobLocationId);

        builder.WithOwner().HasForeignKey(l => l.BlobId);

        builder.Property(l => l.BlobId)
            .HasColumnName("blob_id")
            .HasMaxLength(64)
            .HasConversion(GedValueConverters.BlobId);

        builder.Property(l => l.Provider)
            .HasColumnName("provider")
            .HasMaxLength(StorageProvider.MaxLength)
            .HasConversion(GedValueConverters.StorageProvider);

        builder.Property(l => l.State)
            .HasColumnName("state")
            .HasMaxLength(20)
            .HasConversion(GedValueConverters.LocationState);

        builder.Property(l => l.RegisteredAt).HasColumnName("registered_at");
        builder.Property(l => l.VerifiedAt).HasColumnName("verified_at");

        builder.Ignore(l => l.IsReadable);

        // ObjectKey carries two values, so it cannot go through a single converter. It becomes a
        // nested owned type: two columns, one domain concept, and the domain never has to know how
        // a provider composes an address.
        builder.OwnsOne(l => l.ObjectKey, key =>
        {
            key.Property(k => k.Bucket).HasColumnName("bucket").HasMaxLength(100).IsRequired();
            key.Property(k => k.Key).HasColumnName("object_key")
               .HasMaxLength(ObjectKey.MaxKeyLength).IsRequired();
        });

        builder.Navigation(l => l.ObjectKey).IsRequired();
    }
}
