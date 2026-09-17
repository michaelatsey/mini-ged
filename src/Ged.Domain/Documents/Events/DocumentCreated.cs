

namespace Ged.Domain.Documents.Events;

/// <summary>Raised when a document is created together with its first version.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="FolderId">The folder the document belongs to.</param>
/// <param name="Name">The document's name.</param>
/// <param name="DocType">The document's classification code.</param>
/// <param name="VersionId">The first version.</param>
/// <param name="BlobId">The content digest of the first version.</param>
/// <param name="MimeType">The media type of the first version.</param>
/// <param name="SizeBytes">The size of the first version, in bytes.</param>
/// <param name="CreatedBy">The actor that created the document.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentCreated(
    Guid DocumentId,
    Guid FolderId,
    string Name,
    string DocType,
    Guid VersionId,
    string BlobId,
    string MimeType,
    long SizeBytes,
    string CreatedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);







