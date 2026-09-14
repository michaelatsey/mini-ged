using Ged.Domain.Abstractions;
using Ged.Domain.Blobs.Identifiers;
using Ged.Domain.Documents.Events;
using Ged.Domain.Documents.Identifiers;
using Ged.Domain.Documents.Rules;
using Ged.Domain.Documents.ValueObjects;
using Ged.Domain.Folders.Identifiers;

namespace Ged.Domain.Documents;

/// <summary>
/// A document: a stable logical identity to which successive contents are attached.
/// </summary>
/// <remarks>
/// <para>
/// A document is not a file. It is an identity with a history of versions, each pointing at an
/// immutable content addressed by its digest. Separating the two is what makes versioning,
/// restore, deduplication and provider migration consequences of the model rather than features
/// bolted onto it.
/// </para>
/// <para>
/// The aggregate can create versions but has no way to destroy content. Deleting a document
/// marks it and publishes the digests it referenced; whether any of them is ever removed is a
/// deferred, reversible background decision taken elsewhere.
/// </para>
/// </remarks>
public sealed class Document : AuditableRoot<DocumentId>
{
    private readonly List<DocumentVersion> _versions = [];

    private Document(
        DocumentId id,
        FolderId folderId,
        DocumentName name,
        DocType type,
        DateTimeOffset createdAt,
        Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        FolderId = folderId;
        Name = name;
        Type = type;
    }

    /// <summary>Gets the folder this document belongs to.</summary>
    public FolderId FolderId { get; private set; }

    /// <summary>Gets the document's display name.</summary>
    public DocumentName Name { get; private set; }

    /// <summary>Gets the document's business classification.</summary>
    public DocType Type { get; private set; }

    /// <summary>Gets the identifier of the version currently in force.</summary>
    public DocumentVersionId CurrentVersionId { get; private set; }

    /// <summary>Gets when this document was logically deleted, if it was.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Gets every version of this document, oldest first.</summary>
    public IReadOnlyList<DocumentVersion> Versions => _versions.AsReadOnly();

    /// <summary>Gets a value indicating whether this document is logically deleted.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Gets the version currently in force.</summary>
    public DocumentVersion CurrentVersion => _versions.Single(v => v.Id == CurrentVersionId);

    /// <summary>Creates a document together with its first version.</summary>
    /// <param name="folderId">The folder the document belongs to.</param>
    /// <param name="name">The document's display name.</param>
    /// <param name="type">The document's business classification.</param>
    /// <param name="blobId">The digest of the initial content.</param>
    /// <param name="mimeType">The media type of the initial content.</param>
    /// <param name="sizeBytes">The size of the initial content, in bytes.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The newly created document.</returns>
    /// <remarks>
    /// Content is mandatory at creation: there is no instant at which a document exists without
    /// a version, so the "document with no content" state is not merely rejected — it is
    /// unreachable.
    /// </remarks>
    public static Document Create(
        FolderId folderId,
        DocumentName name,
        DocType type,
        BlobId blobId,
        MimeType mimeType,
        long sizeBytes,
        DateTimeOffset now,
        Actor by)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(mimeType);
        ArgumentNullException.ThrowIfNull(by);

        var document = new Document(DocumentId.New(), folderId, name, type, now, by);

        var version = new DocumentVersion(
            DocumentVersionId.New(), document.Id, VersionNumber.First,
            blobId, name, mimeType, sizeBytes, comment: null, by, now);

        document._versions.Add(version);
        document.CurrentVersionId = version.Id;

        document.RaiseDomainEvent(new DocumentCreated(
            document.Id.Value, folderId.Value, name.Value, type.Code,
            version.Id.Value, blobId.Value, mimeType.Value, sizeBytes, by.Value, now));

        return document;
    }

    /// <summary>Appends a new content version and makes it current.</summary>
    /// <param name="blobId">The digest of the new content.</param>
    /// <param name="fileName">The file name to record with this version.</param>
    /// <param name="mimeType">The media type of the new content.</param>
    /// <param name="sizeBytes">The size of the new content, in bytes.</param>
    /// <param name="comment">An optional comment describing the change.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The version that was appended.</returns>
    public DocumentVersion AddVersion(
        BlobId blobId,
        DocumentName fileName,
        MimeType mimeType,
        long sizeBytes,
        string? comment,
        DateTimeOffset now,
        Actor by)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(mimeType);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new DocumentMustNotBeDeletedRule(DeletedAt));

        var current = CurrentVersion;
        CheckRule(new VersionContentMustDifferFromCurrentRule(current.BlobId, blobId));

        var version = new DocumentVersion(
            DocumentVersionId.New(), Id, current.Number.Next(),
            blobId, fileName, mimeType, sizeBytes, comment, by, now);

        _versions.Add(version);
        CurrentVersionId = version.Id;
        Touch(now, by);

        RaiseDomainEvent(new DocumentVersionAdded(
            Id.Value, version.Id.Value, version.Number.Value,
            blobId.Value, current.BlobId.Value, sizeBytes, by.Value, now));

        return version;
    }

    /// <summary>Brings back the content of an earlier version as a new version.</summary>
    /// <param name="versionId">The version whose content should become current again.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The version that was appended.</returns>
    /// <remarks>
    /// Restoring appends rather than repointing. Moving <c>CurrentVersionId</c> back would erase
    /// the record of everything that happened since, leaving a history that no longer explains
    /// how the document reached its present state.
    /// </remarks>
    public DocumentVersion RestoreVersion(DocumentVersionId versionId, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new DocumentMustNotBeDeletedRule(DeletedAt));

        var target = _versions.Find(v => v.Id == versionId);
        CheckRule(new VersionMustBelongToDocumentRule(Id, target is not null, versionId));
        CheckRule(new VersionMustNotAlreadyBeCurrentRule(CurrentVersionId, versionId));

        return AddVersion(
            target!.BlobId, target.FileName, target.MimeType, target.SizeBytes,
            $"Restored from version {target.Number}", now, by);
    }

    /// <summary>Renames the document.</summary>
    /// <param name="newName">The new display name.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void Rename(DocumentName newName, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(newName);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new DocumentMustNotBeDeletedRule(DeletedAt));

        if (Name == newName) return;

        var previous = Name;
        Name = newName;
        Touch(now, by);

        RaiseDomainEvent(new DocumentRenamed(
            Id.Value, previous.Value, newName.Value, by.Value, now));
    }

    /// <summary>Moves the document to another folder.</summary>
    /// <param name="targetFolderId">The destination folder.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void MoveTo(FolderId targetFolderId, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new DocumentMustNotBeDeletedRule(DeletedAt));

        if (FolderId == targetFolderId) return;

        var previous = FolderId;
        FolderId = targetFolderId;
        Touch(now, by);

        RaiseDomainEvent(new DocumentMoved(
            Id.Value, previous.Value, targetFolderId.Value, by.Value, now));
    }

    /// <summary>Changes the document's business classification.</summary>
    /// <param name="newType">The new classification.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void Reclassify(DocType newType, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(newType);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new DocumentMustNotBeDeletedRule(DeletedAt));

        if (Type == newType) return;

        var previous = Type;
        Type = newType;
        Touch(now, by);

        RaiseDomainEvent(new DocumentReclassified(Id.Value, previous.Code, newType.Code, now));
    }

    /// <summary>Logically deletes the document.</summary>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// No content is touched. The event carries every digest the document referenced so a
    /// background collector knows what to examine — examining is not removing, and another
    /// document may still reference any of them.
    /// </remarks>
    public void SoftDelete(DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        if (IsDeleted) return;

        DeletedAt = now;
        Touch(now, by);

        var blobIds = _versions.Select(v => v.BlobId.Value).Distinct().ToArray();

        RaiseDomainEvent(new DocumentDeleted(Id.Value, FolderId.Value, blobIds, by.Value, now));
    }

    /// <summary>Restores a logically deleted document.</summary>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void Restore(DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new DocumentMustBeDeletedToRestoreRule(DeletedAt));

        DeletedAt = null;
        Touch(now, by);

        RaiseDomainEvent(new DocumentRestored(Id.Value, by.Value, now));
    }
}
