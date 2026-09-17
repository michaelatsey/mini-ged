namespace Ged.Domain.Documents;

/// <summary>Strongly-typed identifier for a document.</summary>
/// <param name="Value">The underlying time-ordered GUID.</param>
public readonly record struct DocumentId(Guid Value) : IEntityId
{
    /// <inheritdoc />
    object IEntityId.Value => Value;

    /// <summary>Creates a new, time-ordered document identifier.</summary>
    /// <returns>A new <see cref="DocumentId"/>.</returns>
    public static DocumentId New() => new(Guid.CreateVersion7());

    /// <summary>Rehydrates a document identifier from an existing value.</summary>
    /// <param name="value">The underlying GUID.</param>
    /// <returns>The corresponding <see cref="DocumentId"/>.</returns>
    /// <exception cref="DomainException">Thrown when <paramref name="value"/> is empty.</exception>
    public static DocumentId From(Guid value) => value == Guid.Empty
        ? throw new DomainException("A document identifier cannot be empty.")
        : new DocumentId(value);

    /// <summary>Returns the identifier as text.</summary>
    /// <returns>The GUID rendered as text.</returns>
    public override string ToString() => Value.ToString();
}
