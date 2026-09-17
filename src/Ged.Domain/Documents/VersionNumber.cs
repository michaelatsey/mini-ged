namespace Ged.Domain.Documents;

/// <summary>A one-based, gapless document version number.</summary>
/// <remarks>
/// The only way to obtain the next number is <see cref="Next"/>, which is what makes the
/// sequence consecutive by construction rather than by convention: no caller can skip a number.
/// </remarks>
public readonly record struct VersionNumber : IValueObject
{
    private VersionNumber(int value) => Value = value;

    /// <summary>Gets the numeric value.</summary>
    public int Value { get; }

    /// <summary>The number carried by a document's first version.</summary>
    public static VersionNumber First { get; } = new(1);

    /// <summary>Rehydrates a version number from an existing value.</summary>
    /// <param name="value">The numeric value.</param>
    /// <returns>The corresponding <see cref="VersionNumber"/>.</returns>
    /// <exception cref="DomainException">Thrown when the value is below one.</exception>
    public static VersionNumber From(int value) => value < 1
        ? throw new DomainException("A version number must be greater than or equal to one.")
        : new VersionNumber(value);

    /// <summary>Returns the number that follows this one.</summary>
    /// <returns>The next version number.</returns>
    public VersionNumber Next() => new(Value + 1);

    /// <summary>Returns the number as text.</summary>
    /// <returns>The numeric value rendered as text.</returns>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
