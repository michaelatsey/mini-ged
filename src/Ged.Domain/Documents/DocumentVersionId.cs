namespace Ged.Domain.Documents;

/// <summary>Strongly-typed identifier for a document version.</summary>
/// <param name="Value">The underlying time-ordered GUID.</param>
public readonly record struct DocumentVersionId(Guid Value) : IEntityId
{
    /// <inheritdoc />
    object IEntityId.Value => Value;

    /// <summary>Creates a new, time-ordered version identifier.</summary>
    /// <returns>A new <see cref="DocumentVersionId"/>.</returns>
    public static DocumentVersionId New() => new(Guid.CreateVersion7());

    /// <summary>Rehydrates a version identifier from an existing value.</summary>
    /// <param name="value">The underlying GUID.</param>
    /// <returns>The corresponding <see cref="DocumentVersionId"/>.</returns>
    /// <exception cref="DomainException">Thrown when <paramref name="value"/> is empty.</exception>
    public static DocumentVersionId From(Guid value) => value == Guid.Empty
        ? throw new DomainException("A document version identifier cannot be empty.")
        : new DocumentVersionId(value);

    /// <summary>Returns the identifier as text.</summary>
    /// <returns>The GUID rendered as text.</returns>
    public override string ToString() => Value.ToString();
}
