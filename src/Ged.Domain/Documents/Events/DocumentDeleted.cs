namespace Ged.Domain.Documents.Events;

/// <summary>
/// Raised when a document is logically deleted.
/// </summary>
/// <param name="DocumentId">The document.</param>
/// <param name="FolderId">The folder the document belonged to.</param>
/// <param name="BlobIds">
/// Every distinct content digest the document referenced. This is a list of contents worth
/// examining, not an instruction to remove them: another document may still reference any of
/// them, and physical removal is a deferred, reversible background decision.
/// </param>
/// <param name="DeletedBy">The actor that deleted the document.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentDeleted(
    Guid DocumentId,
    Guid FolderId,
    string[] BlobIds,
    string DeletedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
