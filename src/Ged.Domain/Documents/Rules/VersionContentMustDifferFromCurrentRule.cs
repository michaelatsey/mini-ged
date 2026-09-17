using Ged.Domain.Blobs;

namespace Ged.Domain.Documents.Rules;

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
