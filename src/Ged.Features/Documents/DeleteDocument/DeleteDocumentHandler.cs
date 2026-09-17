using Ged.Domain.Documents;

namespace Ged.Features.Documents.DeleteDocument;

/// <summary>Logically deletes a document.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="By">The acting identity.</param>
public sealed record DeleteDocumentCommand(Guid DocumentId, string By);

/// <summary>Handles <see cref="DeleteDocumentCommand"/>.</summary>
/// <param name="documents">Loads the document.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
/// <remarks>
/// Three lines of work and no call to storage. Deleting a document breaks its link to content; what
/// happens to the content afterwards is decided days later by the collector, behind a retention
/// window. There is no code path from this use case to a deleted byte.
/// </remarks>
public sealed class DeleteDocumentHandler(
    IDocumentRepository documents, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<bool>> HandleAsync(
        DeleteDocumentCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = await documents.FindAsync(DocumentId.From(command.DocumentId), ct);

        if (document is null)
            return Outcome.NotFound<bool>("Document");

        document.SoftDelete(clock.UtcNow, new Actor(command.By));

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<bool>(true);
    }
}
