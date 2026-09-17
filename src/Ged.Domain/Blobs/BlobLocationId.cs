namespace Ged.Domain.Blobs;

/// <summary>Strongly-typed identifier for a blob location.</summary>
/// <param name="Value">The underlying time-ordered GUID.</param>
public readonly record struct BlobLocationId(Guid Value) : IEntityId
{
    /// <inheritdoc />
    object IEntityId.Value => Value;

    /// <summary>Creates a new, time-ordered location identifier.</summary>
    /// <returns>A new <see cref="BlobLocationId"/>.</returns>
    public static BlobLocationId New() => new(Guid.CreateVersion7());

    /// <summary>Rehydrates a location identifier from an existing value.</summary>
    /// <param name="value">The underlying GUID.</param>
    /// <returns>The corresponding <see cref="BlobLocationId"/>.</returns>
    /// <exception cref="DomainException">Thrown when <paramref name="value"/> is empty.</exception>
    public static BlobLocationId From(Guid value) => value == Guid.Empty
        ? throw new DomainException("A blob location identifier cannot be empty.")
        : new BlobLocationId(value);

    /// <summary>Returns the identifier as text.</summary>
    /// <returns>The GUID rendered as text.</returns>
    public override string ToString() => Value.ToString();
}
