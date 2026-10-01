namespace Ged.Features.Common.FileTypes;

/// <summary>Why an upload was refused.</summary>
public enum RejectionReason
{
    /// <summary>The upload was accepted.</summary>
    None = 0,

    /// <summary>The file carries no bytes.</summary>
    Empty,

    /// <summary>The file is larger than the ceiling for its format.</summary>
    TooLarge,

    /// <summary>The name carries no extension, so nothing declares what it claims to be.</summary>
    NoExtension,

    /// <summary>The extension is not in the allowlist for this environment or document type.</summary>
    ExtensionNotAllowed,

    /// <summary>The client's declared media type contradicts its own extension.</summary>
    DeclaredMediaTypeMismatch,

    /// <summary>The content does not look like what the extension claims.</summary>
    ContentDoesNotMatchExtension,
}

/// <summary>The outcome of inspecting an upload.</summary>
/// <param name="Reason">Why it was refused, or <see cref="RejectionReason.None"/>.</param>
/// <param name="Format">The format the content actually is, when accepted.</param>
/// <param name="Message">A reason the caller can show, deliberately free of internals.</param>
/// <param name="MaxSizeBytes">The ceiling that applies to the resolved format.</param>
/// <param name="Reclassified">
/// Whether <see cref="Format"/> is what the content turned out to be rather than what its name
/// claimed. True only where the policy lets the two stand in for each other.
/// </param>
public sealed record FileTypeDecision(
    RejectionReason Reason,
    FileFormat? Format,
    string Message,
    long MaxSizeBytes = long.MaxValue,
    bool Reclassified = false)
{
    /// <summary>Gets a value indicating whether the upload may proceed.</summary>
    public bool Accepted => Reason == RejectionReason.None;

    /// <summary>The media type to record. Detected, never the one the client declared.</summary>
    /// <remarks>
    /// This is the point of the whole inspection: what gets stored, and later served back in a
    /// <c>Content-Type</c> header, is what the bytes are — not what the uploader said they were.
    /// </remarks>
    public string MediaType => Format?.MediaType ?? "application/octet-stream";

    /// <summary>Creates an accepted decision.</summary>
    /// <param name="format">The detected format.</param>
    /// <param name="maxSizeBytes">The ceiling that applies to this format.</param>
    /// <param name="reclassified">Whether the format came from the content rather than the name.</param>
    /// <returns>The decision.</returns>
    /// <remarks>
    /// The ceiling travels with the decision so the caller can stop reading the request body once it
    /// is exceeded. Checking the size only after the upload has been staged means a 250 MB file
    /// claiming to be a CSV is written to disk in full before being refused for being 25 times over
    /// its limit — which is a cheap way to fill a server's temp volume.
    /// </remarks>
    public static FileTypeDecision Accept(
        FileFormat format, long maxSizeBytes = long.MaxValue, bool reclassified = false) =>
        new(RejectionReason.None, format, "Accepted.", maxSizeBytes, reclassified);

    /// <summary>The name to store, which follows the content rather than the client's claim.</summary>
    /// <param name="fileName">The name the client supplied.</param>
    /// <returns>
    /// That name unchanged when nothing was reclassified, otherwise with its last extension replaced
    /// by the accepted format's own — <c>photo.jpg</c> holding a PNG becomes <c>photo.png</c>, and a
    /// JPEG stored under the <c>jpeg</c> format becomes <c>.jpg</c>, the first extension that format
    /// declares.
    /// </returns>
    /// <remarks>
    /// The last extension, because that is the one the inspection read: <c>invoice.pdf.exe</c> is an
    /// executable, and correcting any other segment would leave the name saying something different
    /// from what was checked. Correcting it at all is the point of forgiving the mismatch — a client
    /// opens a file by its extension, so a PNG handed back as <c>.jpg</c> still opens in the wrong
    /// application.
    /// </remarks>
    public string StoredNameFor(string fileName) =>
        Reclassified && Format is not null && !string.IsNullOrWhiteSpace(fileName)
            ? Path.ChangeExtension(fileName, Format.Extensions[0])
            : fileName;

    /// <summary>Creates a refusal.</summary>
    /// <param name="reason">Why the upload was refused.</param>
    /// <param name="message">The reason to show the caller.</param>
    /// <returns>The decision.</returns>
    public static FileTypeDecision Reject(RejectionReason reason, string message) =>
        new(reason, null, message);

    /// <summary>The reason code the API maps to a status, for logging and problem details.</summary>
    public string Code => Reason.ToString();
}
