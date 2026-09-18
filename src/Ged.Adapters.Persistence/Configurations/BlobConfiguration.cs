using Ged.Adapters.Persistence.Converters;
using Ged.Adapters.Persistence.Providers;
using Ged.Domain.Blobs;

namespace Ged.Adapters.Persistence.Configurations;

internal sealed class BlobConfiguration(IPersistenceProvider provider)
    : IEntityTypeConfiguration<Blob>
{
    public void Configure(EntityTypeBuilder<Blob> builder)
    {
        builder.ToTable("blob");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasMaxLength(64)
            .HasConversion(GedValueConverters.BlobId);

        builder.Property(b => b.SizeBytes).HasColumnName("size_bytes");

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .HasConversion(GedValueConverters.BlobStatus);

        builder.Property(b => b.OrphanSince).HasColumnName("orphan_since");

        // A mapped scalar, never a navigation: the locations are an owned collection of this same
        // aggregate, so a navigation would make EF order the two writes around a relationship it
        // does not need to know about. document.current_version_id is mapped the same way.
        builder.Property(b => b.PrimaryLocationId)
            .HasColumnName("primary_location_id")
            .HasConversion(GedValueConverters.NullableBlobLocationId);

        builder.Property(b => b.CreatedAt).HasColumnName("created_at");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at");

        builder.Property(b => b.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.Actor);

        builder.Property(b => b.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.NullableActor);

        // Locations are owned for the reason the aggregate holds them at all: an owned collection
        // cannot be queried independently, so the aggregate boundary becomes a property of the model
        // rather than a convention, and the locations are never loaded or saved apart from the blob.
        builder.OwnsMany(b => b.Locations, BlobLocationConfiguration.Configure);

        builder.Navigation(b => b.Locations)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.Ignore(b => b.Primary);
        builder.Ignore(b => b.ReadOrder);
        builder.Ignore(b => b.IsPurged);
        builder.Ignore(b => b.DomainEvents);

        // Optimistic concurrency, spelled by the engine in use: a uint onto PostgreSQL's hidden
        // xmin column, a byte array onto a SQL Server rowversion. Both arrive as a shadow
        // property, so no aggregate carries a storage concern in its public API.
        provider.ConfigureConcurrencyToken(builder);
    }
}
