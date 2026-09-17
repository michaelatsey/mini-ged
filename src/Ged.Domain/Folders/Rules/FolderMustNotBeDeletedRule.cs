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
