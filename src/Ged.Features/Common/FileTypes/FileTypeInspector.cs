using System.Text;
using Ged.Core.Ports.FileTypes;
using Microsoft.Extensions.Options;

namespace Ged.Features.Common.FileTypes;

/// <summary>Applies the upload policy to a file name and then to its content.</summary>
/// <param name="detector">Identifies what the content actually is.</param>
/// <param name="options">The configured policy.</param>
/// <remarks>
/// <para>
/// Three layers, in increasing order of cost and of authority:
/// </para>
/// <code>
/// extension          free, and a claim the client makes
/// declared type      free, and a claim the client makes
/// detected format    needs bytes, and is the only evidence
/// </code>
/// <para>
/// The first two exist to refuse cheaply, not to decide. A file named <c>.pdf</c> with an
/// <c>application/pdf</c> header proves nothing — both are strings the uploader chose.
/// </para>
/// <para>
/// The third is delegated. Recognising a format is knowledge about file layouts that a maintained
/// library holds better; deciding whether the answer is acceptable here is business policy that no
/// library can hold at all. This class keeps the second half and only the second half.
/// </para>
/// </remarks>
public sealed class FileTypeInspector(
    IContentFormatDetector detector,
    IOptions<UploadPolicyOptions> options) : IFileTypeInspector
{
    /// <summary>How many leading bytes are captured for the weak-signature checks.</summary>
    public const int HeaderLength = 512;

    private readonly UploadPolicyOptions _policy = options.Value;

    /// <inheritdoc />
    public FileTypeDecision CheckName(string fileName, string? declaredMediaType, string? docType)
    {
        var extension = ExtensionOf(fileName);

        if (extension.Length == 0)
        {
            return FileTypeDecision.Reject(
                RejectionReason.NoExtension, "The file name carries no extension.");
        }

        if (!TryResolveByExtension(extension, docType, out var format) || format is null)
        {
            return FileTypeDecision.Reject(
                RejectionReason.ExtensionNotAllowed,
                $"Files with the '.{extension}' extension are not accepted here.");
        }

        if (_policy.EnforceDeclaredMediaType
            && !string.IsNullOrWhiteSpace(declaredMediaType)
            && !string.Equals(
                declaredMediaType.Split(';')[0].Trim(),
                format.MediaType,
                StringComparison.OrdinalIgnoreCase))
        {
            return FileTypeDecision.Reject(
                RejectionReason.DeclaredMediaTypeMismatch,
                "The declared content type does not match the file extension.");
        }

        // Not accepted yet — only "nothing rules it out so far". The content still decides. The
        // ceiling travels back so the caller can stop reading the body once it is exceeded.
        return FileTypeDecision.Accept(format, CeilingFor(format));
    }

    /// <inheritdoc />
    public FileTypeDecision Inspect(
        string fileName,
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType)
    {
        ArgumentNullException.ThrowIfNull(openContent);

        if (sizeBytes <= 0)
        {
            return FileTypeDecision.Reject(RejectionReason.Empty, "The file is empty.");
        }

        var extension = ExtensionOf(fileName);

        if (!TryResolveByExtension(extension, docType, out var format) || format is null)
        {
            return FileTypeDecision.Reject(
                RejectionReason.ExtensionNotAllowed,
                $"Files with the '.{extension}' extension are not accepted here.");
        }

        var ceiling = CeilingFor(format);

        if (sizeBytes > ceiling)
        {
            return FileTypeDecision.Reject(
                RejectionReason.TooLarge,
                $"The file exceeds the {ceiling / (1024 * 1024)} MB limit for this type.");
        }

        var detected = SafelyDetect(openContent);

        var matches = format.Mode switch
        {
            // The detector recognised nothing AND the bytes look like text. Both halves matter:
            // without the first, an executable renamed .txt is only caught by the NUL check; without
            // the second, an empty answer would be taken as agreement.
            DetectionMode.TextHeuristic => detected is null && LooksLikeText(header),

            // The detected format must be one this catalogue entry accepts. Matched on extension
            // rather than media type: extensions are stable identifiers, while a third-party media
            // type table can carry a typo — and one of them does.
            _ => detected is not null
                 && format.Extensions.Contains(detected.Extension, StringComparer.OrdinalIgnoreCase)
                 && (format.Additional is null || format.Additional(header, sizeBytes)),
        };

        if (matches)
        {
            return FileTypeDecision.Accept(format, ceiling);
        }

        // The detected format is named in the message when there is one. It turns "rejected" into
        // "you sent a spreadsheet named .docx", which is the difference between a support ticket and
        // a user who fixes it themselves.
        var detail = detected is null
            ? $"The content is not a valid {format.Name.ToUpperInvariant()} file."
            : $"The content is a {detected.Extension.ToUpperInvariant()} file, not a {format.Name.ToUpperInvariant()}.";

        return FileTypeDecision.Reject(RejectionReason.ContentDoesNotMatchExtension, detail);
    }

    /// <summary>Runs the detector, treating any failure as "not recognised".</summary>
    /// <param name="openContent">Opens the staged content.</param>
    /// <returns>The detected format, or null when nothing matched or the detector failed.</returns>
    /// <remarks>
    /// <para>
    /// A detector reads bytes an attacker chose. Parsing a container — a truncated ZIP, a compound
    /// file with a corrupt directory — can throw, and a malformed upload must not surface as a 500
    /// with a stack trace from a third-party library.
    /// </para>
    /// <para>
    /// Failing closed is the only safe reading: content the detector could not parse is content
    /// nobody has identified, which is exactly the case this whole class exists to refuse. The
    /// alternative — treating a parse failure as "probably fine" — would make a deliberately
    /// corrupted archive the easiest way past the check.
    /// </para>
    /// </remarks>
    private DetectedFormat? SafelyDetect(Func<Stream> openContent)
    {
        try
        {
            using var content = openContent();

            return detector.Detect(content);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Returns the last extension of a name, lowercase and without its dot.</summary>
    /// <remarks>
    /// The last one, deliberately. <c>invoice.pdf.exe</c> is an executable, and reading anything but
    /// the final segment is how that file gets treated as a PDF.
    /// </remarks>
    private static string ExtensionOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(fileName);

        return extension.Length > 1 ? extension[1..].ToLowerInvariant() : string.Empty;
    }

    private long CeilingFor(FileFormat format) =>
        _policy.MaxSizeByFormat.TryGetValue(format.Name, out var perFormat)
            ? Math.Min(perFormat, _policy.MaxSizeBytes)
            : _policy.MaxSizeBytes;

    private bool TryResolveByExtension(string extension, string? docType, out FileFormat? format)
    {
        format = null;

        if (extension.Length == 0)
        {
            return false;
        }

        // Entries may name a format or a set — "documents" expands to thirteen names. The document
        // type may narrow the global list, never widen it: an unknown classification falls back to
        // the global list, and a known one is intersected with it. Otherwise adding a classification
        // would become a way around the policy.
        IEnumerable<string> allowed = _policy.AllowedFormats.SelectMany(FileFormats.Expand);

        if (docType is not null
            && _policy.FormatsByDocType.TryGetValue(docType, out var narrowed))
        {
            allowed = allowed.Intersect(
                narrowed.SelectMany(FileFormats.Expand), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var name in allowed)
        {
            if (FileFormats.TryGet(name, out var candidate)
                && candidate is not null
                && candidate.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                format = candidate;

                return true;
            }
        }

        return false;
    }

    /// <summary>Decides whether a header plausibly contains text.</summary>
    /// <remarks>
    /// Text formats have no signature, so they cannot be confirmed — only contradicted. A NUL byte is
    /// the contradiction that matters: legal in a binary, essentially absent from text. A UTF-8 BOM
    /// is skipped first, since it is the one leading sequence text may legitimately carry.
    /// </remarks>
    private static bool LooksLikeText(ReadOnlySpan<byte> header)
    {
        if (header.IsEmpty)
        {
            return false;
        }

        var body = header.StartsWith(Encoding.UTF8.Preamble) ? header[Encoding.UTF8.Preamble.Length..] : header;

        foreach (var b in body)
        {
            if (b == 0x00)
            {
                return false;
            }
        }

        return true;
    }
}
