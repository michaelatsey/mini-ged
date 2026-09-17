
namespace Ged.Domain.Folders;

/// <summary>
/// The display name of a folder, validated and normalized.
/// </summary>
/// <param name="Value">The raw name.</param>
/// <remarks>
/// Names are trimmed on construction so that two folders cannot differ by surrounding
/// whitespace alone — visually identical siblings are a support burden, not a feature.
/// Path separators are rejected because a folder name is a label, never a path fragment:
/// allowing them lets a name silently imply a hierarchy the model does not have.
/// </remarks>
public sealed record FolderName(string Value) : IValueObject
{
    /// <summary>The maximum supported length of a folder name.</summary>
    public const int MaxLength = 128;

    private static readonly char[] Forbidden = ['/', '\\', '\0', '\n', '\r'];

    /// <summary>Gets the normalized folder name.</summary>
    public string Value { get; } = Validate(Value);

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("A folder name is required.");

        var trimmed = raw.Trim();

        if (trimmed.Length > MaxLength)
            throw new DomainException($"A folder name cannot exceed {MaxLength} characters.");

        if (trimmed.IndexOfAny(Forbidden) >= 0)
            throw new DomainException("A folder name cannot contain path separators or control characters.");

        if (trimmed is "." or "..")
            throw new DomainException("A folder name cannot be a relative path marker.");

        return trimmed;
    }

    /// <summary>Returns the folder name.</summary>
    /// <returns>The normalized name.</returns>
    public override string ToString() => Value;
}
