using System.IO.Compression;
using System.Text;
using Ged.Core.Ports.FileTypes;

namespace Ged.Adapters.FileTypes;

/// <summary>Identifies content from its bytes, with no third-party dependency.</summary>
/// <remarks>
/// <para>
/// Covers the formats this application accepts and nothing else. That is the point: a detector meant
/// for a closed allowlist has a different job from one meant to identify anything — the first
/// answers "is this what it claims?", the second answers "what on earth is this?", and only the
/// first is needed to validate an upload.
/// </para>
/// <para>
/// Three techniques, because three families of format need three:
/// </para>
/// <code>
/// leading bytes      pdf, png, jpeg, gif, tiff, bmp, rtf, exe, elf
/// ZIP container      docx, xlsx, pptx, odt, ods, odp — one signature, six formats
/// OLE2 container     doc, xls, ppt — one signature, and .msi shares it
/// </code>
/// <para>
/// Executables are recognised on purpose even though no policy accepts them. Identifying a renamed
/// binary produces "this is an EXE" instead of "unrecognised", which is both a better message and a
/// stronger check for the text formats, where nothing recognised is the condition for acceptance.
/// </para>
/// </remarks>
public sealed class BuiltInContentFormatDetector : IContentFormatDetector
{
    private const int HeaderLength = 512;

    /// <summary>How far into a compound file the directory marker is looked for.</summary>
    /// <remarks>
    /// Bounded on purpose: the cost of identification must not depend on the size of the upload, or a
    /// large file becomes a way to make the server work.
    /// </remarks>
    private const int CompoundFileScanLength = 256 * 1024;

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole2 = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly (byte[] Signature, string Extension, string MediaType)[] Leading =
    [
        ([0x25, 0x50, 0x44, 0x46], "pdf", "application/pdf"),                       // %PDF
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "png", "image/png"),
        ([0xFF, 0xD8, 0xFF], "jpg", "image/jpeg"),
        ([0x47, 0x49, 0x46, 0x38, 0x37, 0x61], "gif", "image/gif"),                 // GIF87a
        ([0x47, 0x49, 0x46, 0x38, 0x39, 0x61], "gif", "image/gif"),                 // GIF89a
        ([0x49, 0x49, 0x2A, 0x00], "tif", "image/tiff"),                            // II*
        ([0x4D, 0x4D, 0x00, 0x2A], "tif", "image/tiff"),                            // MM*
        ([0x42, 0x4D], "bmp", "image/bmp"),                                         // BM
        ([0x7B, 0x5C, 0x72, 0x74, 0x66], "rtf", "application/rtf"),                 // {\rtf
        ([0x4D, 0x5A], "exe", "application/vnd.microsoft.portable-executable"),     // MZ
        ([0x7F, 0x45, 0x4C, 0x46], "elf", "application/x-elf"),                     // .ELF
    ];

    /// <inheritdoc />
    public DetectedFormat? Detect(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var buffer = new byte[HeaderLength];
        var read = content.ReadAtLeast(buffer, HeaderLength, throwOnEndOfStream: false);
        var header = buffer.AsSpan(0, read);

        // Containers first: their signatures are shared, so a match there means "look inside",
        // never "done".
        if (header.StartsWith(Zip))
        {
            return DetectZipFlavour(content);
        }

        if (header.StartsWith(Ole2))
        {
            return DetectCompoundFileFlavour(content);
        }

        foreach (var (signature, extension, mediaType) in Leading)
        {
            if (header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature))
            {
                return new DetectedFormat(extension, mediaType);
            }
        }

        // Plain text, CSV and JSON land here, and correctly so: they carry no signature, and saying
        // "nothing matched" is more honest than guessing.
        return null;
    }

    /// <summary>Tells apart the formats that share the ZIP signature.</summary>
    /// <remarks>
    /// <c>PK\x03\x04</c> identifies a ZIP and nothing more — .docx, .xlsx, .pptx, .odt, .ods, .odp,
    /// .jar and .apk are indistinguishable by signature.
    /// <para>
    /// OpenDocument is decided first because its test is exact: the specification requires a first
    /// entry named <c>mimetype</c>, stored uncompressed, holding the media type verbatim. OOXML has
    /// no equivalent, so it is decided by the manifest plus a part prefix.
    /// </para>
    /// <para>
    /// Only entry names are read; nothing is decompressed, so a zip bomb costs nothing here.
    /// </para>
    /// </remarks>
    private static DetectedFormat? DetectZipFlavour(Stream content)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.GetEntry("mimetype") is { Length: <= 128 } mimetype)
        {
            using var stream = mimetype.Open();
            using var reader = new StreamReader(stream, Encoding.ASCII);

            return reader.ReadToEnd().Trim() switch
            {
                "application/vnd.oasis.opendocument.text" =>
                    new("odt", "application/vnd.oasis.opendocument.text"),
                "application/vnd.oasis.opendocument.spreadsheet" =>
                    new("ods", "application/vnd.oasis.opendocument.spreadsheet"),
                "application/vnd.oasis.opendocument.presentation" =>
                    new("odp", "application/vnd.oasis.opendocument.presentation"),
                _ => new DetectedFormat("zip", "application/zip"),
            };
        }

        var hasManifest = false;
        string? flavour = null;

        foreach (var entry in archive.Entries)
        {
            hasManifest |= entry.FullName.Equals("[Content_Types].xml", StringComparison.Ordinal);

            flavour ??= entry.FullName switch
            {
                var n when n.StartsWith("word/", StringComparison.Ordinal) => "docx",
                var n when n.StartsWith("xl/", StringComparison.Ordinal) => "xlsx",
                var n when n.StartsWith("ppt/", StringComparison.Ordinal) => "pptx",
                _ => null,
            };

            if (hasManifest && flavour is not null)
            {
                break;
            }
        }

        return hasManifest && flavour is not null
            ? new DetectedFormat(flavour, MediaTypeFor(flavour))
            : new DetectedFormat("zip", "application/zip");
    }

    /// <summary>Tells apart the legacy Office formats that share the OLE2 signature.</summary>
    /// <remarks>
    /// <c>D0 CF 11 E0</c> identifies the container: .doc, .xls, .ppt and .msi are the same format at
    /// that level. The flavour is carried by a directory stream name, stored as UTF-16LE.
    /// <para>
    /// A bounded prefix is scanned for the marker rather than the allocation table being walked.
    /// Chasing sector chains through hostile input is more attack surface than the answer is worth,
    /// and the layouts real Office files use put the directory well inside the scanned window.
    /// </para>
    /// </remarks>
    private static DetectedFormat? DetectCompoundFileFlavour(Stream content)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var buffer = new byte[CompoundFileScanLength];
        var read = content.ReadAtLeast(buffer, CompoundFileScanLength, throwOnEndOfStream: false);
        var span = buffer.AsSpan(0, read);

        if (span.IndexOf(Encoding.Unicode.GetBytes("WordDocument")) >= 0)
        {
            return new DetectedFormat("doc", "application/msword");
        }

        if (span.IndexOf(Encoding.Unicode.GetBytes("Workbook")) >= 0)
        {
            return new DetectedFormat("xls", "application/vnd.ms-excel");
        }

        if (span.IndexOf(Encoding.Unicode.GetBytes("PowerPoint Document")) >= 0)
        {
            return new DetectedFormat("ppt", "application/vnd.ms-powerpoint");
        }

        // An OLE2 container this application does not accept — a .msi, for instance.
        return new DetectedFormat("ole2", "application/x-ole-storage");
    }

    private static string MediaTypeFor(string flavour) => flavour switch
    {
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/zip",
    };
}
