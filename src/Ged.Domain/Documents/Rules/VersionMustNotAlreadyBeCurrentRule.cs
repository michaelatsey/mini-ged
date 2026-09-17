namespace Ged.Domain.Documents.Rules;

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
