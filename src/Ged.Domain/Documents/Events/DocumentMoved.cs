
namespace Ged.Domain.Documents.Events;

/// <summary>Raised when a document is moved to another folder.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="PreviousFolderId">The folder before the move.</param>
/// <param name="NewFolderId">The folder after the move.</param>
/// <param name="MovedBy">The actor that moved the document.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentMoved(
    Guid DocumentId,
    Guid PreviousFolderId,
    Guid NewFolderId,
    string MovedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
