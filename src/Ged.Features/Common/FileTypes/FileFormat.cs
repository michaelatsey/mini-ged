namespace Ged.Features.Common.FileTypes;

/// <summary>Validates a format's own header beyond what a detector reports.</summary>
/// <param name="header">The first bytes of the content.</param>
/// <param name="sizeBytes">The size actually received.</param>
/// <returns>True when the header is internally consistent.</returns>
public delegate bool HeaderValidator(ReadOnlySpan<byte> header, long sizeBytes);

/// <summary>How a format is recognised.</summary>
public enum DetectionMode
{
    /// <summary>The detector identifies it from its bytes.</summary>
    Detector = 0,

    /// <summary>
    /// The format carries no signature — plain text, CSV, JSON — so no detector can confirm it. It is
    /// validated negatively instead: the detector must recognise <em>nothing</em>, and the content
    /// must carry no NUL byte. An executable renamed .txt fails both, which is the case worth
    /// catching.
    /// </summary>
    TextHeuristic = 1,
}

/// <summary>A file format this application accepts, and what it is stored as.</summary>
/// <param name="Name">The key used in configuration.</param>
/// <param name="MediaType">
/// The media type recorded on the version. This project's value, never the detector's — a
/// third-party table is not a contract, and one of them has a typo in its OpenDocument entry.
/// </param>
/// <param name="Extensions">The extensions accepted for this format, lowercase, without a dot.</param>
/// <param name="Mode">How the format is recognised.</param>
/// <param name="Additional">
/// An extra consistency check, for formats whose signature is too weak to stand alone.
/// </param>
/// <param name="CarriesExecutableContent">
/// Whether the format can carry code — macros, embedded objects — that a client application may run
/// on open.
/// </param>
/// <remarks>
/// What remains here after detection was delegated is the part nobody else can maintain: which
/// formats this business accepts, what they are stored as, and which ones carry a risk worth naming.
/// The byte-level knowledge left with the detector.
/// </remarks>
public sealed record FileFormat(
    string Name,
    string MediaType,
    IReadOnlyList<string> Extensions,
    DetectionMode Mode = DetectionMode.Detector,
    HeaderValidator? Additional = null,
    bool CarriesExecutableContent = false);

/// <summary>The formats this application accepts.</summary>
public static class FileFormats
{
    private static readonly FileFormat[] All =
    [
        // ── documents ──────────────────────────────────────────────────────────────
        new("pdf", "application/pdf", ["pdf"]),

        // Office Open XML — ZIP containers, told apart by the detector.
        new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ["docx"]),
        new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ["xlsx"]),
        new("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", ["pptx"]),

        // OpenDocument — ZIP containers too.
        new("odt", "application/vnd.oasis.opendocument.text", ["odt"]),
        new("ods", "application/vnd.oasis.opendocument.spreadsheet", ["ods"]),
        new("odp", "application/vnd.oasis.opendocument.presentation", ["odp"]),

        // Legacy Office — OLE2 compound files, sharing one signature with .msi.
        //
        // Flagged as carrying executable content: the binary formats embed VBA natively, with no
        // .docm-style extension to warn anyone. See docs/uploads.md.
        new("doc", "application/msword", ["doc"], CarriesExecutableContent: true),
        new("xls", "application/vnd.ms-excel", ["xls"], CarriesExecutableContent: true),
        new("ppt", "application/vnd.ms-powerpoint", ["ppt"], CarriesExecutableContent: true),

        // RTF embeds OLE objects, which is what made it a long-running exploit vector.
        new("rtf", "application/rtf", ["rtf"], CarriesExecutableContent: true),

        new("txt", "text/plain", ["txt", "md"], DetectionMode.TextHeuristic),
        new("csv", "text/csv", ["csv"], DetectionMode.TextHeuristic),
        new("json", "application/json", ["json"], DetectionMode.TextHeuristic),

        // ── images ─────────────────────────────────────────────────────────────────
        new("png", "image/png", ["png"]),
        new("jpeg", "image/jpeg", ["jpg", "jpeg"]),
        new("gif", "image/gif", ["gif"]),
        new("tiff", "image/tiff", ["tif", "tiff"]),

        // A BMP signature is two ASCII letters, far too common to mean anything on its own. The
        // header also carries the file size at offset 2, so requiring the two to agree turns a
        // 2-byte coincidence into a 6-byte one. Kept even though the detector recognises bitmaps:
        // the check costs four comparisons and does not depend on a third party.
        new("bmp", "image/bmp", ["bmp"], Additional: BitmapSizeMatchesHeader),
    ];

    private static readonly Dictionary<string, FileFormat> ByName =
        All.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Named sets, so configuration can say "documents" instead of listing thirteen names.</summary>
    private static readonly Dictionary<string, string[]> Groups =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx",
                             "odt", "ods", "odp", "txt", "csv", "rtf"],
            ["images"] = ["jpeg", "png", "tiff", "bmp", "gif"],
            ["office"] = ["doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp"],
            ["text"] = ["txt", "csv", "json"],
        };

    /// <summary>Gets every known format.</summary>
    public static IReadOnlyList<FileFormat> Known => All;

    /// <summary>Gets the names of the sets configuration may use.</summary>
    public static IReadOnlyCollection<string> GroupNames => Groups.Keys;

    /// <summary>Finds a format by its configuration name.</summary>
    /// <param name="name">The format name.</param>
    /// <param name="format">The format, when known.</param>
    /// <returns>True when the name matches a known format.</returns>
    public static bool TryGet(string name, out FileFormat? format) =>
        ByName.TryGetValue(name, out format);

    /// <summary>Expands a configured entry into format names.</summary>
    /// <param name="nameOrGroup">A format name, or a set name such as "documents".</param>
    /// <returns>The format names, or an empty sequence when the entry is unknown.</returns>
    public static IEnumerable<string> Expand(string nameOrGroup) =>
        Groups.TryGetValue(nameOrGroup, out var group) ? group
        : ByName.ContainsKey(nameOrGroup) ? [nameOrGroup]
        : [];

    /// <summary>Determines whether a configured entry names a format or a set.</summary>
    /// <param name="nameOrGroup">The entry to test.</param>
    /// <returns>True when it resolves to at least one format.</returns>
    public static bool IsKnown(string nameOrGroup) =>
        ByName.ContainsKey(nameOrGroup) || Groups.ContainsKey(nameOrGroup);

    private static bool BitmapSizeMatchesHeader(ReadOnlySpan<byte> header, long sizeBytes)
    {
        if (header.Length < 6)
        {
            return false;
        }

        var declared = (uint)(header[2] | (header[3] << 8) | (header[4] << 16) | (header[5] << 24));

        return declared == sizeBytes;
    }
}
