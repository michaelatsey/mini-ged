using Ged.Core.Ports.FileTypes;

namespace Ged.Features.Tests.Fakes;

/// <summary>
/// A detector that always answers the same thing, so a test can simply state what the bytes are.
/// </summary>
/// <param name="detected">What every call reports, or null for "nothing recognised".</param>
/// <remarks>
/// Recognising a format from its bytes belongs to the detector; what the policy does with that
/// answer is a separate question, and the only one a stub makes testable without carrying a real
/// file of every format into the repository. Separating the two is all this stub does — it stands
/// in for the detector, never for coverage of it: neither <c>BuiltInContentFormatDetector</c> nor
/// <c>FileSignaturesContentDetector</c> has a test, so the signature tables they carry are
/// unexercised and a green run here says nothing about them.
/// </remarks>
internal sealed class StubDetector(DetectedFormat? detected) : IContentFormatDetector
{
    public DetectedFormat? Detect(Stream content) => detected;
}
