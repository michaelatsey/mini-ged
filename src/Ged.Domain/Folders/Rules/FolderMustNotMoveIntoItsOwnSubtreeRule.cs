namespace Ged.Domain.Folders.Rules;

/// <summary>
/// Prevents a folder from being moved beneath one of its own descendants, which would detach
/// the resulting cycle from the rest of the hierarchy.
/// </summary>
/// <param name="folderId">The folder being moved.</param>
/// <param name="targetAncestry">The ancestry of the proposed new parent.</param>
public sealed class FolderMustNotMoveIntoItsOwnSubtreeRule(
    FolderId folderId, FolderAncestry targetAncestry) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => targetAncestry.Contains(folderId);

    /// <inheritdoc />
    public override string Message => "A folder cannot be moved into its own subtree.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [folderId, targetAncestry.Self];
}
