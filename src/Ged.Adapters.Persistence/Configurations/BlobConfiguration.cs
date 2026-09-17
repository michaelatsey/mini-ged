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

        // Locations are owned for the reason the aggregate holds them at all: promoting one and
        // demoting another has to happen in a single SaveChanges, and an owned collection is what
        // guarantees they are never loaded or saved apart.
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
