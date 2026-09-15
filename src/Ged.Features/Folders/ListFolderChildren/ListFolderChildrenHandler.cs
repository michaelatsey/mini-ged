namespace Ged.Features.Folders.ListFolderChildren;

/// <summary>One child folder in a listing.</summary>
/// <param name="Id">The folder.</param>
/// <param name="Name">Its display name.</param>
/// <param name="FolderType">Its classification.</param>
/// <param name="DocumentCount">How many live documents it holds.</param>
public sealed record FolderListItem(Guid Id, string Name, string FolderType, long DocumentCount);

/// <summary>Lists the direct children of a folder, or the roots.</summary>
/// <param name="connections">Opens a read connection.</param>
public sealed class ListFolderChildrenHandler(IDbConnectionFactory connections)
{
    private const string ChildrenSql = """
        SELECT      f.id          AS id,
                    f.name        AS name,
                    f.folder_type AS folder_type,
                    (SELECT COUNT(*) FROM document d
                      WHERE d.folder_id = f.id AND d.deleted_at IS NULL) AS documents
        FROM        folder f
        WHERE       f.deleted_at IS NULL
          AND     ((@parentId IS NULL AND f.parent_id IS NULL)
                OR (@parentId IS NOT NULL AND f.parent_id = @parentId))
        ORDER BY    f.name
        """;

    /// <summary>Runs the query.</summary>
    /// <param name="parentId">The parent, or null to list the roots.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The children.</returns>
    public async Task<IReadOnlyList<FolderListItem>> HandleAsync(
        Guid? parentId, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = ChildrenSql;
        command.AddParameter("parentId", parentId);

        var items = new List<FolderListItem>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            items.Add(new FolderListItem(
                reader.GetGuid("id"),
                reader.GetString("name"),
                reader.GetString("folder_type"),
                reader.GetInt64("documents")));
        }

        return items;
    }
}
