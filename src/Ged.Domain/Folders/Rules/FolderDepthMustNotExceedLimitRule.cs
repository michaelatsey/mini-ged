namespace Ged.Domain.Folders.Rules;

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
