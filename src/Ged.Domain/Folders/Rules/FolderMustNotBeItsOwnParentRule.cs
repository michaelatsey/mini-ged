namespace Ged.Domain.Folders.Rules;

/// <summary>Prevents a folder from being made its own parent.</summary>
/// <param name="folderId">The folder being moved.</param>
/// <param name="targetParentId">The proposed parent.</param>
public sealed class FolderMustNotBeItsOwnParentRule(FolderId folderId, FolderId targetParentId)
    : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => folderId == targetParentId;

    /// <inheritdoc />
    public override string Message => "A folder cannot be its own parent.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [folderId, targetParentId];
}
