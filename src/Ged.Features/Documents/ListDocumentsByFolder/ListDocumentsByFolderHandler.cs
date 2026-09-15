using System.Globalization;
namespace Ged.Features.Documents.ListDocumentsByFolder;

/// <summary>One row of a folder's document list.</summary>
/// <param name="Id">The document.</param>
/// <param name="Name">Its display name.</param>
/// <param name="DocType">Its classification.</param>
/// <param name="MimeType">The media type of the version in force.</param>
/// <param name="SizeBytes">Its size, in bytes.</param>
/// <param name="UpdatedAt">When it last changed, falling back to creation.</param>
public sealed record DocumentListItem(
    Guid Id, string Name, string DocType, string MimeType, long SizeBytes, DateTimeOffset UpdatedAt);

/// <summary>A page of results.</summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Items">The rows.</param>
/// <param name="Total">How many rows exist in total.</param>
/// <param name="Skip">How many were skipped.</param>
/// <param name="Take">How many were requested.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, long Total, int Skip, int Take);

/// <summary>Lists the documents of one folder.</summary>
/// <param name="connections">Opens a read connection.</param>
/// <remarks>
/// Paging uses <c>OFFSET … FETCH FIRST</c>, which is ANSI and runs unchanged on both PostgreSQL and
/// SQL Server — unlike <c>LIMIT</c> or <c>TOP</c>, either of which would drag a dialect into a slice
/// that has no business knowing which engine it is on.
/// </remarks>
public sealed class ListDocumentsByFolderHandler(IDbConnectionFactory connections)
{
    private const string CountSql = """
        SELECT COUNT(*)
        FROM   document
        WHERE  folder_id = @folderId
          AND  deleted_at IS NULL
        """;

    private const string PageSql = """
        SELECT      d.id         AS id,
                    d.name       AS name,
                    d.doc_type   AS doc_type,
                    v.mime_type  AS mime_type,
                    v.size_bytes AS size_bytes,
                    COALESCE(d.updated_at, d.created_at) AS updated_at
        FROM        document         d
        INNER JOIN  document_version v ON v.id = d.current_version_id
        WHERE       d.folder_id = @folderId
          AND       d.deleted_at IS NULL
        ORDER BY    d.name
        OFFSET      @skip ROWS
        FETCH FIRST @take ROWS ONLY
        """;

    /// <summary>Runs the query.</summary>
    /// <param name="folderId">The folder.</param>
    /// <param name="skip">How many rows to skip.</param>
    /// <param name="take">How many rows to return, capped at 200.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The page.</returns>
    public async Task<Page<DocumentListItem>> HandleAsync(
        Guid folderId, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        await using var connection = await connections.OpenAsync(ct);

        long total;

        await using (var counting = connection.CreateCommand())
        {
            counting.CommandText = CountSql;
            counting.AddParameter("folderId", folderId);

            total = Convert.ToInt64(await counting.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        var items = new List<DocumentListItem>();

        await using (var paging = connection.CreateCommand())
        {
            paging.CommandText = PageSql;
            paging.AddParameter("folderId", folderId);
            paging.AddParameter("skip", skip);
            paging.AddParameter("take", take);

            await using var reader = await paging.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                items.Add(new DocumentListItem(
                    reader.GetGuid("id"),
                    reader.GetString("name"),
                    reader.GetString("doc_type"),
                    reader.GetString("mime_type"),
                    reader.GetInt64("size_bytes"),
                    reader.GetInstant("updated_at")));
            }
        }

        return new Page<DocumentListItem>(items, total, skip, take);
    }
}
