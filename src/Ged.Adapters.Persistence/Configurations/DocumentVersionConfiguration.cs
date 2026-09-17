using Ged.Adapters.Persistence.Converters;
using Ged.Domain.Documents;

namespace Ged.Adapters.Persistence.Configurations;

internal static class DocumentVersionConfiguration
{
    public static void Configure(OwnedNavigationBuilder<Document, DocumentVersion> builder)
    {
        builder.ToTable("document_version");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasConversion(GedValueConverters.DocumentVersionId);

        builder.WithOwner().HasForeignKey(v => v.DocumentId);

        builder.Property(v => v.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(GedValueConverters.DocumentId);

        builder.Property(v => v.Number)
            .HasColumnName("version_no")
            .HasConversion(GedValueConverters.VersionNumber);

        builder.Property(v => v.BlobId)
            .HasColumnName("blob_id")
            .HasMaxLength(64)
            .HasConversion(GedValueConverters.BlobId);

        builder.Property(v => v.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(DocumentName.MaxLength)
            .HasConversion(GedValueConverters.DocumentName);

        builder.Property(v => v.MimeType)
            .HasColumnName("mime_type")
            .HasMaxLength(255)
            .HasConversion(GedValueConverters.MimeType);

        builder.Property(v => v.SizeBytes).HasColumnName("size_bytes");
        builder.Property(v => v.Comment).HasColumnName("comment");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");

        builder.Property(v => v.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(Actor.MaxLength)
            .HasConversion(GedValueConverters.Actor);

        // Ordering is part of the aggregate's meaning: a version list out of order makes
        // CurrentVersion and the restore comment read as nonsense.
        builder.HasIndex(v => new { v.DocumentId, v.Number }).IsUnique();
    }
}
