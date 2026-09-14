using Ged.Domain.Folders.Identifiers;

namespace Ged.Domain.Folders.Rules;

/// <summary>Prevents any mutation of a logically deleted folder.</summary>
/// <param name="deletedAt">The folder's deletion instant, if any.</param>
public sealed class FolderMustNotBeDeletedRule(DateTimeOffset? deletedAt) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => deletedAt is not null;

    /// <inheritdoc />
    public override string Message => "A deleted folder cannot be modified.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [deletedAt];
}

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

/// <summary>Enforces the maximum hierarchy depth.</summary>
/// <param name="resultingDepth">The depth the folder would occupy after the operation.</param>
public sealed class FolderDepthMustNotExceedLimitRule(int resultingDepth) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => resultingDepth > FolderAncestry.MaxDepth;

    /// <inheritdoc />
    public override string Message =>
        $"A folder hierarchy cannot exceed {FolderAncestry.MaxDepth} levels.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [resultingDepth];
}

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

/// <summary>Prevents restoring a folder that is not deleted.</summary>
/// <param name="deletedAt">The folder's deletion instant, if any.</param>
public sealed class FolderMustBeDeletedToRestoreRule(DateTimeOffset? deletedAt) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => deletedAt is null;

    /// <inheritdoc />
    public override string Message => "The folder is not deleted and cannot be restored.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [deletedAt];
}
