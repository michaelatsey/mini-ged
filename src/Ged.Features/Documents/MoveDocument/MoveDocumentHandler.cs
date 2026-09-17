using Ged.Domain.Documents;
using Ged.Domain.Folders;

namespace Ged.Features.Documents.MoveDocument;

/// <summary>Moves a document to another folder.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="TargetFolderId">The destination folder.</param>
/// <param name="By">The acting identity.</param>
public sealed record MoveDocumentCommand(Guid DocumentId, Guid TargetFolderId, string By);

/// <summary>Handles <see cref="MoveDocumentCommand"/>.</summary>
/// <param name="documents">Loads the document.</param>
/// <param name="folders">Verifies the destination.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
/// <remarks>
/// The destination is checked here rather than in the aggregate: a document cannot see whether a
/// folder exists, and handing it a folder id it cannot verify would be an invariant in name only.
/// </remarks>
public sealed class MoveDocumentHandler(
    IDocumentRepository documents, IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<bool>> HandleAsync(
        MoveDocumentCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = await documents.FindAsync(DocumentId.From(command.DocumentId), ct);

        if (document is null || document.IsDeleted)
            return Outcome.NotFound<bool>("Document");

        var targetId = FolderId.From(command.TargetFolderId);
        var target = await folders.FindAsync(targetId, ct);

        if (target is null || target.IsDeleted)
            return Outcome.NotFound<bool>("Target folder");

        document.MoveTo(targetId, clock.UtcNow, new Actor(command.By));

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<bool>(true);
    }
}
