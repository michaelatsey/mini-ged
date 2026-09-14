using Ged.Domain.Blobs.Identifiers;
using Ged.Domain.Documents.Identifiers;

namespace Ged.Domain.Documents.Rules;

/// <summary>Prevents any mutation of a logically deleted document.</summary>
/// <param name="deletedAt">The document's deletion instant, if any.</param>
public sealed class DocumentMustNotBeDeletedRule(DateTimeOffset? deletedAt) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => deletedAt is not null;

    /// <inheritdoc />
    public override string Message => "A deleted document cannot be modified.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [deletedAt];
}

/// <summary>
/// Prevents appending a version whose content is identical to the current one.
/// </summary>
/// <param name="currentBlobId">The content of the current version.</param>
/// <param name="newBlobId">The content being appended.</param>
/// <remarks>
/// Without this rule an unchanged re-upload produces a version that records no change, which
/// pollutes the history that audit and restore both depend on.
/// </remarks>
public sealed class VersionContentMustDifferFromCurrentRule(BlobId currentBlobId, BlobId newBlobId)
    : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => currentBlobId == newBlobId;

    /// <inheritdoc />
    public override string Message => "The submitted content is identical to the current version.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [currentBlobId, newBlobId];
}

/// <summary>Ensures a version belongs to the document it is being restored on.</summary>
/// <param name="documentId">The document.</param>
/// <param name="known">Whether the version was found on the document.</param>
/// <param name="candidate">The version being restored.</param>
public sealed class VersionMustBelongToDocumentRule(
    DocumentId documentId, bool known, DocumentVersionId candidate) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => !known;

    /// <inheritdoc />
    public override string Message =>
        $"Version {candidate} does not belong to document {documentId}.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [documentId, candidate];
}

/// <summary>Prevents restoring the version that is already current.</summary>
/// <param name="currentVersionId">The current version.</param>
/// <param name="candidate">The version being restored.</param>
public sealed class VersionMustNotAlreadyBeCurrentRule(
    DocumentVersionId currentVersionId, DocumentVersionId candidate) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => currentVersionId == candidate;

    /// <inheritdoc />
    public override string Message => "This version is already the current version.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [currentVersionId, candidate];
}

/// <summary>Prevents restoring a document that is not deleted.</summary>
/// <param name="deletedAt">The document's deletion instant, if any.</param>
public sealed class DocumentMustBeDeletedToRestoreRule(DateTimeOffset? deletedAt) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => deletedAt is null;

    /// <inheritdoc />
    public override string Message => "The document is not deleted and cannot be restored.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [deletedAt];
}
