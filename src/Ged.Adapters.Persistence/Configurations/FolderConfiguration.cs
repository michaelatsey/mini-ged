using Ged.Adapters.Persistence.Converters;
using Ged.Domain.Folders;
using Ged.Domain.Folders.ValueObjects;

namespace Ged.Adapters.Persistence.Configurations;

internal sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folder");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id)
            .HasColumnName("id")
            .HasConversion(GedValueConverters.FolderId);

        builder.Property(f => f.ParentId)
            .HasColumnName("parent_id")
            .HasConversion(GedValueConverters.NullableFolderId);

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .HasMaxLength(FolderName.MaxLength)
            .HasConversion(GedValueConverters.FolderName);

        builder.Property(f => f.Type)
            .HasColumnName("folder_type")
            .HasMaxLength(50)
            .HasConversion(GedValueConverters.FolderType);

        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.Property(f => f.DeletedAt).HasColumnName("deleted_at");

        builder.Property(f => f.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.Actor);

        builder.Property(f => f.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.NullableActor);

        // The parent is a reference to another aggregate, so it is a plain column and never a
        // navigation. A navigation here would let one folder load its whole branch, which is the
        // aggregate boundary this model exists to keep.
        builder.Ignore(f => f.IsRoot);
        builder.Ignore(f => f.IsDeleted);
        builder.Ignore(f => f.DomainEvents);

        builder.UseXminAsConcurrencyToken();
    }
}
