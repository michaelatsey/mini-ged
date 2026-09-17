
namespace Ged.Domain.Documents.Rules;

/// <summary>Prevents any mutation of a logically deleted document.</summary>
/// <param name="deletedAt">The document's deletion instant, if any.</param>
public sealed class DocumentMustNotBeDeletedRule(DateTimeOffset? deletedAt) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => deletedAt is not null;

    /// <inheritdoc />
    public override string Message => "A deleted document cannot be modified.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [deletedAt];
}
