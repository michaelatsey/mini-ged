using Ged.Domain.Folders;

namespace Ged.Features.Folders.DeleteFolder;

/// <summary>Logically deletes an empty folder.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="By">The acting identity.</param>
public sealed record DeleteFolderCommand(Guid FolderId, string By);

/// <summary>Handles <see cref="DeleteFolderCommand"/>.</summary>
/// <param name="folders">Loads the folder.</param>
/// <param name="connections">Establishes whether the folder still holds anything.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
/// <remarks>
/// Emptiness is established here and handed to the aggregate. Child folders and documents are
/// separate aggregates a folder cannot see, so the handler asks the question and the domain decides
/// what the answer means. Nothing cascades: deleting a tree is an orchestration, not an aggregate
/// operation.
/// </remarks>
public sealed class DeleteFolderHandler(
    IFolderRepository folders,
    IDbConnectionFactory connections,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string ContentsSql = """
        SELECT
            (SELECT COUNT(*) FROM folder   WHERE parent_id = @id AND deleted_at IS NULL) AS folders,
            (SELECT COUNT(*) FROM document WHERE folder_id = @id AND deleted_at IS NULL) AS documents
        """;

    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<bool>> HandleAsync(
        DeleteFolderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var folder = await folders.FindAsync(FolderId.From(command.FolderId), ct);

        if (folder is null)
            return Outcome.NotFound<bool>("Folder");

        bool hasChildFolders, hasDocuments;

        await using (var connection = await connections.OpenAsync(ct))
        await using (var counting = connection.CreateCommand())
        {
            counting.CommandText = ContentsSql;
            counting.AddParameter("id", command.FolderId);

            await using var reader = await counting.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);

            hasChildFolders = reader.GetInt64("folders") > 0;
            hasDocuments = reader.GetInt64("documents") > 0;
        }

        folder.SoftDelete(hasChildFolders, hasDocuments, clock.UtcNow, new Actor(command.By));

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<bool>(true);
    }
}
