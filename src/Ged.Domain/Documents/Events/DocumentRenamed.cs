namespace Ged.Domain.Documents.Events;

/// <summary>Raised when a document is renamed.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="PreviousName">The name before the change.</param>
/// <param name="NewName">The name after the change.</param>
/// <param name="RenamedBy">The actor that renamed the document.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentRenamed(
    Guid DocumentId,
    string PreviousName,
    string NewName,
    string RenamedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
