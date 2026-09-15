namespace Ged.Features.Documents.GetDocumentById;

/// <summary>What one document screen needs.</summary>
/// <param name="Id">The document.</param>
/// <param name="Name">Its display name.</param>
/// <param name="DocType">Its classification.</param>
/// <param name="FolderId">The folder it belongs to.</param>
/// <param name="FolderName">That folder's name.</param>
/// <param name="CurrentVersionNo">The ordinal of the version in force.</param>
/// <param name="MimeType">The media type of that version.</param>
/// <param name="SizeBytes">Its size, in bytes.</param>
/// <param name="VersionCount">How many versions exist.</param>
/// <param name="CreatedAt">When the document was created.</param>
/// <param name="CreatedBy">Who created it.</param>
/// <param name="UpdatedAt">When it was last modified, if ever.</param>
public sealed record DocumentDetail(
    Guid Id,
    string Name,
    string DocType,
    Guid FolderId,
    string FolderName,
    int CurrentVersionNo,
    string MimeType,
    long SizeBytes,
    int VersionCount,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? UpdatedAt);

/// <summary>Reads one document for display.</summary>
/// <param name="connections">Opens a read connection.</param>
/// <remarks>
/// <para>
/// No repository, no change tracking, no aggregate. A repository exists to reconstitute an aggregate
/// so a rule can be applied to it; nothing here will be mutated or saved, so loading a
/// <c>Document</c> with its whole version history would be work thrown away.
/// </para>
/// <para>
/// The SQL lives in this file, not in a shared query class. Changing what this screen shows then
/// touches one folder, which is the entire point of slicing vertically — and no other slice inherits
/// a column it did not ask for.
/// </para>
/// </remarks>
public sealed class GetDocumentByIdHandler(IDbConnectionFactory connections)
{
    private const string Sql = """
        SELECT  d.id                 AS id,
                d.name               AS name,
                d.doc_type           AS doc_type,
                f.id                 AS folder_id,
                f.name               AS folder_name,
                v.version_no         AS version_no,
                v.mime_type          AS mime_type,
                v.size_bytes         AS size_bytes,
                d.created_at         AS created_at,
                d.created_by         AS created_by,
                d.updated_at         AS updated_at,
                (SELECT COUNT(*) FROM document_version dv WHERE dv.document_id = d.id)
                                     AS version_count
        FROM        document         d
        INNER JOIN  folder           f ON f.id = d.folder_id
        INNER JOIN  document_version v ON v.id = d.current_version_id
        WHERE   d.id = @id
          AND   d.deleted_at IS NULL
        """;

    /// <summary>Runs the query.</summary>
    /// <param name="id">The document.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<DocumentDetail>> HandleAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = Sql;
        command.AddParameter("id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return Outcome.NotFound<DocumentDetail>("Document");

        return Outcome.Ok<DocumentDetail>(new DocumentDetail(
            reader.GetGuid("id"),
            reader.GetString("name"),
            reader.GetString("doc_type"),
            reader.GetGuid("folder_id"),
            reader.GetString("folder_name"),
            reader.GetInt32("version_no"),
            reader.GetString("mime_type"),
            reader.GetInt64("size_bytes"),
            reader.GetInt32("version_count"),
            reader.GetInstant("created_at"),
            reader.GetString("created_by"),
            reader.GetNullableInstant("updated_at")));
    }
}
