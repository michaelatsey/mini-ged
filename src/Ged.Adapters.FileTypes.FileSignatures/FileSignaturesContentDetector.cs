using FileSignatures;
using Ged.Core.Ports.FileTypes;

namespace Ged.Adapters.FileTypes.FileSignatures;

/// <summary>Identifies content formats with the FileSignatures library.</summary>
/// <param name="inspector">
/// The library's inspector. Build one and share it: it performs work at construction, so building
/// one per request pays that cost on every upload.
/// </param>
/// <remarks>
/// <para>
/// The alternative to <c>BuiltInContentFormatDetector</c>, for the one thing a maintained library
/// does better than hand-written code: knowing about file layouts. Its formats are a type hierarchy
/// with distinct entries for docx, xlsx, pptx, odt, ods and odp, which means its inspectors look
/// inside the container rather than stopping at the shared signature — and from version 7 it reads
/// OLE2 compound files through OpenMcdf, which is what separates .doc from .xls from .msi.
/// </para>
/// <para>
/// Only the extension crosses back into the application. The library's media types are not used: the
/// application records its own, from a catalogue it reviews. That keeps the value stored in the
/// database under this project's control and insulates it from a typo in a third-party table.
/// </para>
/// </remarks>
public sealed class FileSignaturesContentDetector(IFileFormatInspector inspector) : IContentFormatDetector
{
    /// <inheritdoc />
    public DetectedFormat? Detect(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The library reads from the current position; staged content may have been read already.
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var format = inspector.DetermineFileFormat(content);

        if (format is null)
        {
            return null;
        }

        // TrimStart is defensive: the library's contract does not promise whether the extension
        // carries its dot, and a leading '.' would silently fail every comparison in the policy —
        // a failure that refuses valid uploads rather than accepting invalid ones, but a failure.
        return new DetectedFormat(
            format.Extension.TrimStart('.').ToLowerInvariant(),
            format.MediaType);
    }
}
