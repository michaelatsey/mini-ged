namespace Ged.Domain.Documents;

/// <summary>The media type of a document version's content.</summary>
/// <param name="Value">The raw media type.</param>
/// <remarks>
/// The domain validates the shape only. Establishing that the declared type matches the actual
/// bytes is an ingestion concern — it requires reading the content, which the domain never does.
/// </remarks>
public sealed record MimeType(string Value) : IValueObject
{
    /// <summary>Gets the normalized, lowercase media type.</summary>
    public string Value { get; } = Validate(Value);

    /// <summary>The PDF media type.</summary>
    public static MimeType Pdf { get; } = new("application/pdf");

    /// <summary>The fallback media type for unidentified binary content.</summary>
    public static MimeType Octet { get; } = new("application/octet-stream");

    /// <summary>Gets a value indicating whether this is the PDF media type.</summary>
    public bool IsPdf => Value == "application/pdf";

    /// <summary>Gets a value indicating whether this is an image media type.</summary>
    public bool IsImage => Value.StartsWith("image/", StringComparison.Ordinal);

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "application/octet-stream";

        var value = raw.Trim().ToLowerInvariant();
        var slash = value.IndexOf('/', StringComparison.Ordinal);

        return slash > 0 && slash < value.Length - 1
            ? value
            : throw new DomainException($"Invalid media type: {raw}");
    }

    /// <summary>Returns the media type.</summary>
    /// <returns>The normalized media type.</returns>
    public override string ToString() => Value;
}
