using Ged.Adapters.Persistence.Converters;
using Ged.Domain.Documents;
using Ged.Domain.Documents.ValueObjects;

using Ged.Adapters.Persistence.Providers;

namespace Ged.Adapters.Persistence.Configurations;

internal sealed class DocumentConfiguration(IPersistenceProvider provider)
    : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("document");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(GedValueConverters.DocumentId);

        builder.Property(d => d.FolderId)
            .HasColumnName("folder_id")
            .HasConversion(GedValueConverters.FolderId);

        builder.Property(d => d.Name)
            .HasColumnName("name")
            .HasMaxLength(DocumentName.MaxLength)
            .HasConversion(GedValueConverters.DocumentName);

        builder.Property(d => d.Type)
            .HasColumnName("doc_type")
            .HasMaxLength(50)
            .HasConversion(GedValueConverters.DocType);

        builder.Property(d => d.CurrentVersionId)
            .HasColumnName("current_version_id")
            .HasConversion(GedValueConverters.DocumentVersionId);

        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");
        builder.Property(d => d.DeletedAt).HasColumnName("deleted_at");

        builder.Property(d => d.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.Actor);

        builder.Property(d => d.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.NullableActor);

        // Versions are owned: they have no life outside their document, and mapping them as an
        // owned collection means EF cannot query them independently. The aggregate boundary becomes
        // a property of the model rather than a convention someone has to respect.
        builder.OwnsMany(d => d.Versions, DocumentVersionConfiguration.Configure);

        builder.Navigation(d => d.Versions)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        builder.Ignore(d => d.CurrentVersion);
        builder.Ignore(d => d.IsDeleted);
        builder.Ignore(d => d.DomainEvents);

        // Optimistic concurrency, spelled by the engine in use: a uint onto PostgreSQL's hidden
        // xmin column, a byte array onto a SQL Server rowversion. Both arrive as a shadow
        // property, so no aggregate carries a storage concern in its public API.
        provider.ConfigureConcurrencyToken(builder);
    }
}
