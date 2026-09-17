using Ged.Features.Common.FileTypes;

namespace Ged.Features.Tests.Fakes;

/// <summary>
/// An inspector that accepts everything as a PDF. Detection has its own tests; these ones are
/// about what happens to the blob once the content has been accepted.
/// </summary>
internal sealed class PdfInspector : IFileTypeInspector
{
    private static readonly FileFormat Pdf = new("pdf", "application/pdf", ["pdf"]);

    public FileTypeDecision CheckName(string fileName, string? declaredMediaType, string? docType) =>
        FileTypeDecision.Accept(Pdf, 10 * 1024 * 1024);

    public FileTypeDecision Inspect(
        string fileName,
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType) =>
        FileTypeDecision.Accept(Pdf, 10 * 1024 * 1024);
}
