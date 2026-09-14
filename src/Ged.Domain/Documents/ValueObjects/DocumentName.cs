namespace Ged.Domain.Documents.ValueObjects;

/// <summary>The display name of a document, validated and normalized.</summary>
/// <param name="Value">The raw name.</param>
/// <remarks>
/// Names are trimmed on construction so two documents cannot differ by surrounding whitespace
/// alone. Path separators and relative-path markers are rejected because a name is a label,
/// never a path fragment: downstream layers build export archives and file names from it, and
/// the domain must not hand them a value that can escape a directory.
/// </remarks>
public sealed record DocumentName(string Value) : IValueObject
{
    /// <summary>The maximum supported length of a document name.</summary>
    public const int MaxLength = 255;

    private static readonly char[] Forbidden = ['/', '\\', '\0', '\n', '\r'];

    /// <summary>Gets the normalized document name.</summary>
    public string Value { get; } = Validate(Value);

    /// <summary>Gets the lowercase extension without its leading dot, or an empty string.</summary>
    public string Extension =>
        Path.GetExtension(Value) is { Length: > 1 } ext ? ext[1..].ToLowerInvariant() : string.Empty;

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("A document name is required.");

        var trimmed = raw.Trim();

        if (trimmed.Length > MaxLength)
            throw new DomainException($"A document name cannot exceed {MaxLength} characters.");

        if (trimmed.IndexOfAny(Forbidden) >= 0)
            throw new DomainException("A document name cannot contain path separators or control characters.");

        if (trimmed is "." or "..")
            throw new DomainException("A document name cannot be a relative path marker.");

        return trimmed;
    }

    /// <summary>Returns the document name.</summary>
    /// <returns>The normalized name.</returns>
    public override string ToString() => Value;
}
