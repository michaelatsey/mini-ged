namespace Ged.Domain.Documents.ValueObjects;

/// <summary>The business classification of a document.</summary>
/// <param name="Code">The raw classification code.</param>
/// <remarks>
/// Modelled as a value object rather than an <see langword="enum"/> so the persisted value stays
/// readable, so new codes do not require renumbering, and so an unrecognized code fails loudly
/// instead of silently landing on the zero member.
/// </remarks>
public sealed record DocType(string Code) : IValueObject
{
    private static readonly HashSet<string> Allowed =
        new(["UNKNOWN", "CONTRACT", "INVOICE", "REPORT", "CORRESPONDENCE"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the normalized classification code.</summary>
    public string Code { get; } = Validate(Code);

    /// <summary>A document whose classification has not been established.</summary>
    public static DocType Unknown { get; } = new("UNKNOWN");

    /// <summary>A contractual document.</summary>
    public static DocType Contract { get; } = new("CONTRACT");

    /// <summary>An invoice or billing document.</summary>
    public static DocType Invoice { get; } = new("INVOICE");

    /// <summary>A report.</summary>
    public static DocType Report { get; } = new("REPORT");

    /// <summary>Inbound or outbound correspondence.</summary>
    public static DocType Correspondence { get; } = new("CORRESPONDENCE");

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "UNKNOWN";

        var code = raw.Trim().ToUpperInvariant();

        return Allowed.Contains(code)
            ? code
            : throw new DomainException($"Unknown document type: {raw}");
    }

    /// <summary>Returns the classification code.</summary>
    /// <returns>The normalized code.</returns>
    public override string ToString() => Code;
}
