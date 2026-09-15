using System.Globalization;
using Ged.Domain.Folders;
using Ged.Domain.Folders.Identifiers;

namespace Ged.Features.Folders.GetFolderById;

/// <summary>What one folder screen needs, breadcrumb included.</summary>
/// <param name="Id">The folder.</param>
/// <param name="ParentId">Its parent, or null when it is a root.</param>
/// <param name="Name">Its display name.</param>
/// <param name="FolderType">Its classification.</param>
/// <param name="ChildFolderCount">How many live child folders it holds.</param>
/// <param name="DocumentCount">How many live documents it holds.</param>
/// <param name="Breadcrumb">The chain from the root down to this folder.</param>
public sealed record FolderDetail(
    Guid Id,
    Guid? ParentId,
    string Name,
    string FolderType,
    long ChildFolderCount,
    long DocumentCount,
    IReadOnlyList<BreadcrumbEntry> Breadcrumb);

/// <summary>One step of a breadcrumb.</summary>
/// <param name="Id">The folder.</param>
/// <param name="Name">Its display name.</param>
public sealed record BreadcrumbEntry(Guid Id, string Name);

/// <summary>Reads one folder for display.</summary>
/// <param name="connections">Opens a read connection.</param>
/// <param name="folders">Supplies the ancestry chain.</param>
/// <remarks>
/// <para>
/// The breadcrumb reuses <c>IFolderRepository.GetAncestryAsync</c> rather than repeating the
/// recursive walk here. The recursive syntax differs between engines — PostgreSQL requires
/// <c>RECURSIVE</c>, SQL Server rejects it — and that difference belongs to the persistence adapter,
/// not to a slice.
/// </para>
/// <para>
/// Names are then fetched with an expanded parameter list rather than an array parameter, which
/// SQL Server has no equivalent for. Two round trips, and the slice stays portable.
/// </para>
/// </remarks>
public sealed class GetFolderByIdHandler(IDbConnectionFactory connections, IFolderRepository folders)
{
    private const string DetailSql = """
        SELECT  f.id          AS id,
                f.parent_id   AS parent_id,
                f.name        AS name,
                f.folder_type AS folder_type,
                (SELECT COUNT(*) FROM folder   c WHERE c.parent_id = f.id AND c.deleted_at IS NULL)
                              AS child_folders,
                (SELECT COUNT(*) FROM document d WHERE d.folder_id = f.id AND d.deleted_at IS NULL)
                              AS documents
        FROM    folder f
        WHERE   f.id = @id
          AND   f.deleted_at IS NULL
        """;

    /// <summary>Runs the query.</summary>
    /// <param name="id">The folder.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<FolderDetail>> HandleAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);

        Guid folderId;
        Guid? parentId;
        string name, folderType;
        long childFolders, documents;

        await using (var detail = connection.CreateCommand())
        {
            detail.CommandText = DetailSql;
            detail.AddParameter("id", id);

            await using var reader = await detail.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
                return Outcome.NotFound<FolderDetail>("Folder");

            folderId = reader.GetGuid("id");
            parentId = reader.GetNullableGuid("parent_id");
            name = reader.GetString("name");
            folderType = reader.GetString("folder_type");
            childFolders = reader.GetInt64("child_folders");
            documents = reader.GetInt64("documents");
        }

        var ancestry = await folders.GetAncestryAsync(FolderId.From(id), ct);
        var breadcrumb = ancestry is null
            ? []
            : await ReadNamesAsync(connection, ancestry, ct);

        return Outcome.Ok<FolderDetail>(new FolderDetail(
            folderId, parentId, name, folderType, childFolders, documents, breadcrumb));
    }

    private static async Task<IReadOnlyList<BreadcrumbEntry>> ReadNamesAsync(
        System.Data.Common.DbConnection connection, FolderAncestry ancestry, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();

        var placeholders = new List<string>(ancestry.Depth);

        for (var i = 0; i < ancestry.Chain.Count; i++)
        {
            var parameter = $"id{i.ToString(CultureInfo.InvariantCulture)}";
            placeholders.Add($"@{parameter}");
            command.AddParameter(parameter, ancestry.Chain[i].Value);
        }

        command.CommandText =
            $"SELECT id AS id, name AS name FROM folder WHERE id IN ({string.Join(", ", placeholders)})";

        var byId = new Dictionary<Guid, string>();

        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                byId[reader.GetGuid("id")] = reader.GetString("name");
        }

        // The chain carries the order; the lookup only carries the names.
        return [.. ancestry.Chain
            .Where(f => byId.ContainsKey(f.Value))
            .Select(f => new BreadcrumbEntry(f.Value, byId[f.Value]))];
    }
}
