namespace Ged.Core.Ports.FileTypes;

/// <summary>Identifies what content actually is, from its bytes alone.</summary>
/// <remarks>
/// <para>
/// One question, and only one: <em>what is this?</em> Whether the answer is acceptable — in this
/// environment, for this document type, under which size ceiling — is a policy decision and belongs
/// to the application, not here.
/// </para>
/// <para>
/// That split is what makes the detector replaceable. Recognising a format is a body of knowledge
/// about file layouts that someone else maintains better; deciding what to accept is business rules
/// that nobody else can maintain at all.
/// </para>
/// </remarks>
public interface IContentFormatDetector
{
    /// <summary>Identifies the format of the supplied content.</summary>
    /// <param name="content">
    /// A readable, seekable stream positioned anywhere. The implementation rewinds it before reading
    /// and leaves the position undefined afterwards; callers that need it reset should reopen.
    /// </param>
    /// <returns>The detected format, or null when the content matches nothing known.</returns>
    /// <remarks>
    /// A null result is not an error. Plain text, CSV and JSON carry no signature at all, so "nothing
    /// matched" is the correct answer for them — and the caller decides what that means.
    /// </remarks>
    DetectedFormat? Detect(Stream content);
}
