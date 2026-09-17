
namespace Ged.Domain.Documents.Events;

/// <summary>Raised when a logically deleted document is restored.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="RestoredBy">The actor that restored the document.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentRestored(
    Guid DocumentId,
    string RestoredBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
