namespace Ged.Domain.Documents.Rules;

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
