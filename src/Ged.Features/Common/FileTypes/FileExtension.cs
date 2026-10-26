namespace Ged.Features.Common.FileTypes;

/// <summary>Decides what counts as the extension of a file name a client supplied.</summary>
/// <remarks>
/// <para>
/// What follows the last dot is an extension only if it looks like one: 1 to
/// <see cref="MaxLength"/> ASCII letters or digits, at least one of them a letter. Anything else leaves
/// the name with no extension at all.
/// </para>
/// <para>
/// <c>Path.GetExtension</c> takes whatever follows the last dot, so a name a client shortened as
/// <c>servic(...) - du 01_01_2026</c> claimed the extension <c>) - du 01_01_2026</c> and was refused, and
/// <c>rapport v1.2</c> claimed <c>2</c>. The letter is what tells a version or a year from an extension;
/// every extension the catalogue knows has one, and none is longer than four characters.
/// </para>
/// <para>
/// One rule for both readings — what the name claims, and what the stored name replaces. Two copies
/// would drift, and the drift costs data rather than a refusal: a name read as extensionless but cut at
/// its last dot when stored loses everything after that dot.
/// </para>
/// </remarks>
internal static class FileExtension
{
    /// <summary>The longest tail still read as an extension.</summary>
    public const int MaxLength = 10;

    /// <summary>Returns the extension a name claims, lowercase and without its dot.</summary>
    /// <param name="fileName">The name as the client supplied it.</param>
    /// <returns>The extension, or an empty string when the name claims none.</returns>
    public static string Of(string fileName)
    {
        var dot = IndexOfDot(fileName);

        return dot < 0 ? string.Empty : fileName[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>Gives a name the extension of the format its content was accepted as.</summary>
    /// <param name="fileName">The name as the client supplied it.</param>
    /// <param name="extension">The extension to give it, without its dot.</param>
    /// <returns>
    /// The name with its extension replaced when it claims one, otherwise with the extension appended —
    /// after dropping a trailing dot, so <c>rapport.</c> becomes <c>rapport.pdf</c>, not
    /// <c>rapport..pdf</c>.
    /// </returns>
    public static string Replace(string fileName, string extension)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        var dot = IndexOfDot(fileName);

        var stem = dot >= 0
            ? fileName[..dot]
            : fileName.EndsWith('.') ? fileName[..^1] : fileName;

        return $"{stem}.{extension}";
    }

    /// <summary>Returns the position of the dot introducing a plausible extension, or -1.</summary>
    /// <remarks>
    /// The last dot, deliberately: <c>invoice.pdf.exe</c> is an executable, and reading anything but the
    /// final segment is how that file gets treated as a PDF. A leading dot counts — <c>.bashrc</c> names
    /// an extension, not an absence of one.
    /// </remarks>
    private static int IndexOfDot(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return -1;
        }

        var dot = fileName.LastIndexOf('.');

        return dot >= 0 && IsPlausible(fileName.AsSpan(dot + 1)) ? dot : -1;
    }

    private static bool IsPlausible(ReadOnlySpan<char> tail)
    {
        if (tail.IsEmpty || tail.Length > MaxLength)
        {
            return false;
        }

        var hasLetter = false;

        foreach (var c in tail)
        {
            if (char.IsAsciiLetter(c))
            {
                hasLetter = true;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return hasLetter;
    }
}
