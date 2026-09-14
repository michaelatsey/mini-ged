using Ged.Domain.Abstractions;
using Ged.Domain.Blobs.Identifiers;
using Ged.Domain.Documents.Identifiers;
using Ged.Domain.Documents.ValueObjects;

namespace Ged.Domain.Documents;

/// <summary>
/// A document's content frozen at one instant: the pairing of a version number with the digest
/// of the bytes that were current then.
/// </summary>
/// <remarks>
/// <para>
/// Immutable by construction. Every member is <c>private init</c> and no method mutates state,
/// so a version cannot be rewritten after the fact — which is what allows several documents to
/// point at the same content without one of them being able to alter what the others see.
/// </para>
/// <para>
/// The constructor is <see langword="internal"/> so <see cref="Document"/> is the only possible
/// author. A version detached from its document has no meaning and must not be constructible.
/// </para>
/// </remarks>
public sealed class DocumentVersion : Entity<DocumentVersionId>
{
    internal DocumentVersion(
        DocumentVersionId id,
        DocumentId documentId,
        VersionNumber number,
        BlobId blobId,
        DocumentName fileName,
        MimeType mimeType,
        long sizeBytes,
        string? comment,
        Actor createdBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(mimeType);
        ArgumentNullException.ThrowIfNull(createdBy);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        DocumentId = documentId;
        Number = number;
        BlobId = blobId;
        FileName = fileName;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        Comment = comment?.Trim() is { Length: > 0 } c ? c : null;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the document this version belongs to.</summary>
    public DocumentId DocumentId { get; private init; }

    /// <summary>Gets this version's ordinal within the document.</summary>
    public VersionNumber Number { get; private init; }

    /// <summary>Gets the digest of the content this version points at.</summary>
    public BlobId BlobId { get; private init; }

    /// <summary>Gets the file name captured when this version was created.</summary>
    public DocumentName FileName { get; private init; }

    /// <summary>Gets the media type of this version's content.</summary>
    public MimeType MimeType { get; private init; }

    /// <summary>Gets the size of this version's content, in bytes.</summary>
    public long SizeBytes { get; private init; }

    /// <summary>Gets the optional comment recorded with this version.</summary>
    public string? Comment { get; private init; }

    /// <summary>Gets the actor that created this version.</summary>
    public Actor CreatedBy { get; private init; }

    /// <summary>Gets the instant this version was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private init; }
}
