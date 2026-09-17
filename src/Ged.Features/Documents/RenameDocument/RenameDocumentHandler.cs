using Ged.Domain.Documents;

namespace Ged.Features.Documents.RenameDocument;

/// <summary>Renames a document.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="NewName">The new display name.</param>
/// <param name="By">The acting identity.</param>
public sealed record RenameDocumentCommand(Guid DocumentId, string NewName, string By);

/// <summary>Handles <see cref="RenameDocumentCommand"/>.</summary>
/// <param name="documents">Loads the document.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
public sealed class RenameDocumentHandler(
    IDocumentRepository documents, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<bool>> HandleAsync(
        RenameDocumentCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = await documents.FindAsync(DocumentId.From(command.DocumentId), ct);

        if (document is null || document.IsDeleted)
            return Outcome.NotFound<bool>("Document");

        document.Rename(new DocumentName(command.NewName), clock.UtcNow, new Actor(command.By));

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<bool>(true);
    }
}
