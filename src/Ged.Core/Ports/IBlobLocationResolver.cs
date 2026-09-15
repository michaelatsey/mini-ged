using Ged.Domain.Blobs;

namespace Ged.Core.Ports;

/// <summary>Turns a blob into readable content, whichever backend currently holds it.</summary>
/// <remarks>
/// This is the mechanism that makes a storage migration invisible. It tries the primary location,
/// then verified replicas, then legacy copies, and reports the locations it had to skip rather than
/// deciding on the caller's behalf what they mean. Callers ask for content, never for a provider.
/// </remarks>
public interface IBlobLocationResolver
{
    /// <summary>Opens the content of a blob.</summary>
    /// <param name="blob">The blob to read.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The content and the locations that were skipped to obtain it.</returns>
    /// <exception cref="ObjectNotFoundException">
    /// Thrown when no location of the blob could serve the content.
    /// </exception>
    Task<BlobContent> OpenAsync(Blob blob, CancellationToken ct = default);
}
