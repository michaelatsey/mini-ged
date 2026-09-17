namespace Ged.Domain.Folders.Rules;

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
