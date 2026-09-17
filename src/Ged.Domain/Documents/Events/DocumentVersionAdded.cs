namespace Ged.Domain.Documents.Events;

/// <summary>Raised when a new content version is appended to a document.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="VersionId">The new version.</param>
/// <param name="VersionNumber">The new version's ordinal.</param>
/// <param name="BlobId">The content digest of the new version.</param>
/// <param name="PreviousBlobId">
/// The content digest the document pointed at before. Carried so a consumer knows immediately
/// which content may have become orphaned, without rescanning.
/// </param>
/// <param name="SizeBytes">The size of the new version, in bytes.</param>
/// <param name="CreatedBy">The actor that appended the version.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentVersionAdded(
    Guid DocumentId,
    Guid VersionId,
    int VersionNumber,
    string BlobId,
    string PreviousBlobId,
    long SizeBytes,
    string CreatedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
