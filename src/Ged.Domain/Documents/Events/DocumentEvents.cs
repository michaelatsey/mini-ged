using Ged.Domain.Abstractions;

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

/// <summary>Raised when a document's business classification changes.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="PreviousType">The classification before the change.</param>
/// <param name="NewType">The classification after the change.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentReclassified(
    Guid DocumentId,
    string PreviousType,
    string NewType,
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

/// <summary>Raised when a logically deleted document is restored.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="RestoredBy">The actor that restored the document.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record DocumentRestored(
    Guid DocumentId,
    string RestoredBy,
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);
