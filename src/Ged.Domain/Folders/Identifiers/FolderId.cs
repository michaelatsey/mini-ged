namespace Ged.Domain.Folders.Identifiers;

/// <summary>Strongly-typed identifier for a folder.</summary>
/// <param name="Value">The underlying time-ordered GUID.</param>
public readonly record struct FolderId(Guid Value) : IEntityId
{
    /// <inheritdoc />
    object IEntityId.Value => Value;

    /// <summary>Creates a new, time-ordered folder identifier.</summary>
    /// <returns>A new <see cref="FolderId"/>.</returns>
    public static FolderId New() => new(Guid.CreateVersion7());

    /// <summary>Rehydrates a folder identifier from an existing value.</summary>
    /// <param name="value">The underlying GUID.</param>
    /// <returns>The corresponding <see cref="FolderId"/>.</returns>
    /// <exception cref="DomainException">Thrown when <paramref name="value"/> is empty.</exception>
    public static FolderId From(Guid value) => value == Guid.Empty
        ? throw new DomainException("A folder identifier cannot be empty.")
        : new FolderId(value);

    /// <summary>Returns the identifier as text.</summary>
    /// <returns>The GUID rendered as text.</returns>
    public override string ToString() => Value.ToString();
}
