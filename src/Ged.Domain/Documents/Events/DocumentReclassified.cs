namespace Ged.Domain.Documents.Events;

/// <summary>Raised when a document's business classification changes.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="PreviousType">The classification before the change.</param>
/// <param name="NewType">The classification after the change.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentReclassified(
    Guid DocumentId,
    string PreviousType,
    string NewType,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
