using Ged.Adapters.Persistence.Providers;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ged.Adapters.Persistence.PostgreSql;

/// <summary>PostgreSQL dialect and conventions.</summary>
internal sealed class PostgreSqlPersistenceProvider : IPersistenceProvider
{
    /// <inheritdoc />
    public string Name => "PostgreSql";

    /// <inheritdoc />
    public string JsonColumnType => "jsonb";

    /// <inheritdoc />
    public string InstantColumnType => "timestamptz";

    /// <inheritdoc />
    /// <remarks>
    /// Every PostgreSQL row already carries a hidden <c>xmin</c> system column holding the id of
    /// the transaction that last wrote it, so a concurrency token costs no column and no schema
    /// change. Since provider version 7.0 this is configured through the standard EF API — a
    /// <see langword="uint"/> property marked <c>IsRowVersion()</c> — and the provider-specific
    /// <c>UseXminAsConcurrencyToken</c> is obsolete.
    /// <para>
    /// One caveat worth knowing before relying on it: <c>xmin</c> values are not preserved across
    /// <c>pg_dump</c>/<c>pg_restore</c>, so a token held by a client across a restore is stale.
    /// That is harmless here, where tokens live only for the duration of a request.
    /// </para>
    /// </remarks>
    public void ConfigureConcurrencyToken(EntityTypeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<uint>("Version").IsRowVersion();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c> hands each dispatcher rows nobody else holds and moves past
    /// the rest instead of queueing behind them.
    /// </remarks>
    public string ClaimPendingOutboxSql => """
        SELECT id          AS Id,
               occurred_at AS OccurredAt,
               type        AS Type,
               payload     AS Payload,
               attempts    AS Attempts
        FROM   outbox_message
        WHERE  processed_at IS NULL
        ORDER  BY occurred_at
        LIMIT  @BatchSize
        FOR UPDATE SKIP LOCKED;
        """;

    /// <inheritdoc />
    public string MarkOutboxProcessedSql => """
        UPDATE outbox_message
        SET    processed_at = @ProcessedAt,
               error        = NULL
        WHERE  id = ANY(@Ids);
        """;

    /// <inheritdoc />
    public string FolderAncestrySql => """
        WITH RECURSIVE chain AS (
            SELECT id, parent_id, 1 AS depth
            FROM   folder
            WHERE  id = @FolderId

            UNION ALL

            SELECT f.id, f.parent_id, c.depth + 1
            FROM   folder f
            JOIN   chain  c ON f.id = c.parent_id
            WHERE  c.depth < @MaxDepth
        )
        SELECT id
        FROM   chain
        ORDER  BY depth DESC;
        """;
}
