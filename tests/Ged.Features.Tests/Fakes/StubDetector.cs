using Ged.Core.Ports.FileTypes;

namespace Ged.Features.Tests.Fakes;

/// <summary>
/// A detector that always answers the same thing, so a test can simply state what the bytes are.
/// </summary>
/// <param name="detected">What every call reports, or null for "nothing recognised".</param>
/// <remarks>
/// Recognising a format from its bytes belongs to the detector and has its own tests. What the
/// policy does with the answer is a separate question, and the only one a stub makes testable
/// without carrying a real file of every format into the repository.
/// </remarks>
internal sealed class StubDetector(DetectedFormat? detected) : IContentFormatDetector
{
    public DetectedFormat? Detect(Stream content) => detected;
}
