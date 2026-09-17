namespace Ged.Domain.Documents.Rules;

/// <summary>Prevents restoring a document that is not deleted.</summary>
/// <param name="deletedAt">The document's deletion instant, if any.</param>
public sealed class DocumentMustBeDeletedToRestoreRule(DateTimeOffset? deletedAt) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => deletedAt is null;

    /// <inheritdoc />
    public override string Message => "The document is not deleted and cannot be restored.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [deletedAt];
}
