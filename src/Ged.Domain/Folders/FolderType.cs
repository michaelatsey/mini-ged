namespace Ged.Domain.Folders;

/// <summary>The business classification of a folder.</summary>
/// <param name="Code">The raw classification code.</param>
public sealed record FolderType(string Code) : IValueObject
{
    private static readonly HashSet<string> Allowed =
        new(["UNKNOWN", "CASE", "CATEGORY", "ARCHIVE"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the normalized classification code.</summary>
    public string Code { get; } = Validate(Code);

    /// <summary>A folder whose classification has not been established.</summary>
    public static FolderType Unknown { get; } = new("UNKNOWN");

    /// <summary>A business case folder, the usual container for documents.</summary>
    public static FolderType Case { get; } = new("CASE");

    /// <summary>An organizational folder that groups other folders.</summary>
    public static FolderType Category { get; } = new("CATEGORY");

    /// <summary>A folder holding content kept for retention purposes.</summary>
    public static FolderType Archive { get; } = new("ARCHIVE");

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "UNKNOWN";

        var code = raw.Trim().ToUpperInvariant();

        return Allowed.Contains(code)
            ? code
            : throw new DomainException($"Unknown folder type: {raw}");
    }

    /// <summary>Returns the classification code.</summary>
    /// <returns>The normalized code.</returns>
    public override string ToString() => Code;
}
