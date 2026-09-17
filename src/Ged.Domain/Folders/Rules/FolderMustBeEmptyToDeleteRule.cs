namespace Ged.Domain.Folders.Rules;

/// <summary>Prevents deletion of a folder that still holds content.</summary>
/// <param name="hasChildFolders">Whether at least one non-deleted child folder remains.</param>
/// <param name="hasDocuments">Whether at least one non-deleted document remains.</param>
/// <remarks>
/// Both facts come from outside the aggregate: child folders and documents are separate
/// aggregates that a folder cannot see. The caller establishes the facts, the rule decides what
/// they mean — which keeps the decision in the domain even though the data is not.
/// </remarks>
public sealed class FolderMustBeEmptyToDeleteRule(bool hasChildFolders, bool hasDocuments)
    : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => hasChildFolders || hasDocuments;

    /// <inheritdoc />
    public override string Message =>
        "A folder that still contains folders or documents cannot be deleted.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [hasChildFolders, hasDocuments];
}
