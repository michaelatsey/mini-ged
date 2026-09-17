namespace Ged.Features.Common.FileTypes;

/// <summary>Decides whether an upload may be stored, and what it actually is.</summary>
/// <remarks>
/// <para>
/// Two stages, because they do not cost the same. <see cref="CheckName"/> uses only the file name
/// and can run before a single byte is read, so a 200 MB executable is refused at the door instead
/// of being buffered to disk first. <see cref="Inspect"/> looks at the content and is authoritative.
/// </para>
/// <para>
/// A local contract rather than a port in <c>Ged.Core</c>: two slices in this assembly use it and
/// nothing outside does. It would move only if an adapter had to implement it — a hosted malware
/// scanner, for instance.
/// </para>
/// </remarks>
public interface IFileTypeInspector
{
    /// <summary>Checks what can be checked before reading the content.</summary>
    /// <param name="fileName">The name supplied by the client. Untrusted.</param>
    /// <param name="declaredMediaType">The media type supplied by the client. Untrusted.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <returns>A refusal, or <see cref="RejectionReason.None"/> to continue.</returns>
    FileTypeDecision CheckName(string fileName, string? declaredMediaType, string? docType);

    /// <summary>Decides on the staged content.</summary>
    /// <param name="fileName">The name supplied by the client.</param>
    /// <param name="sizeBytes">The size actually received, not the one declared.</param>
    /// <param name="header">The first bytes of the content.</param>
    /// <param name="openContent">Opens the staged content, for formats that need more than a header.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <returns>The decision, carrying the detected format when accepted.</returns>
    FileTypeDecision Inspect(
        string fileName,
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType);
}
