using Ged.Adapters.Persistence.Outbox;

namespace Ged.Adapters.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.OccurredAt).HasColumnName("occurred_at");
        builder.Property(m => m.Type).HasColumnName("type").HasMaxLength(300);
        builder.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        builder.Property(m => m.Attempts).HasColumnName("attempts");
        builder.Property(m => m.Error).HasColumnName("error");
    }
}
