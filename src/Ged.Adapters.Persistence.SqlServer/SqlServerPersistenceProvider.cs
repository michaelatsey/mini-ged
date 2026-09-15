using Ged.Adapters.Persistence.Providers;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ged.Adapters.Persistence.SqlServer;

/// <summary>SQL Server dialect and conventions.</summary>
internal sealed class SqlServerPersistenceProvider : IPersistenceProvider
{
    /// <inheritdoc />
    public string Name => "SqlServer";

    /// <inheritdoc />
    /// <remarks>
    /// <c>nvarchar(max)</c> rather than the native JSON type, so the schema runs on every supported
    /// edition. The outbox payload is written once and read once; there is no query against its
    /// contents that would justify a type with narrower availability.
    /// </remarks>
    public string JsonColumnType => "nvarchar(max)";

    /// <inheritdoc />
    public string InstantColumnType => "datetimeoffset(7)";

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server uses a <c>rowversion</c> column — a monotonic 8-byte value the engine maintains
    /// per database. The CLR type is a byte array, unlike PostgreSQL's <see langword="uint"/>, which
    /// is precisely why this member exists on the provider rather than in shared configuration.
    /// </remarks>
    public void ConfigureConcurrencyToken(EntityTypeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<byte[]>("RowVersion")
            .HasColumnName("row_version")
            .IsRowVersion();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>READPAST</c> skips rows another transaction holds, <c>UPDLOCK</c> takes the lock this
    /// worker needs to claim them, and <c>ROWLOCK</c> keeps the engine from escalating to a page
    /// lock and stealing rows a sibling dispatcher would have taken. All three are required:
    /// <c>READPAST</c> alone lets two workers claim the same row.
    /// </remarks>
    public string ClaimPendingOutboxSql => """
        SELECT TOP (@BatchSize)
               id          AS Id,
               occurred_at AS OccurredAt,
               type        AS Type,
               payload     AS Payload,
               attempts    AS Attempts
        FROM   outbox_message WITH (UPDLOCK, READPAST, ROWLOCK)
        WHERE  processed_at IS NULL
        ORDER  BY occurred_at;
        """;

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server has no array parameter, so the identifiers are expanded by the data-access layer
    /// into an <c>IN</c> list rather than bound as one value.
    /// </remarks>
    public string MarkOutboxProcessedSql => """
        UPDATE outbox_message
        SET    processed_at = @ProcessedAt,
               error        = NULL
        WHERE  id IN @Ids;
        """;

    /// <inheritdoc />
    /// <remarks>
    /// The same recursive walk as PostgreSQL, without the <c>RECURSIVE</c> keyword, which SQL Server
    /// rejects. <c>MAXRECURSION</c> is a second guard behind the depth predicate.
    /// </remarks>
    public string FolderAncestrySql => """
        WITH chain AS (
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
        ORDER  BY depth DESC
        OPTION (MAXRECURSION 64);
        """;
}
