using Ged.Domain.Blobs;

namespace Ged.Core.Ports;

/// <summary>A backend that can hand out a time-limited direct read URL.</summary>
/// <remarks>
/// A separate interface rather than a method on <see cref="IObjectStorage"/>: not every backend can
/// do this, and a core contract that assumes the richest implementation forces every other adapter
/// to throw. Callers test for the capability and stream through the application when it is absent.
/// </remarks>
public interface IPresignCapable
{
    /// <summary>Issues a time-limited URL granting read access to one object.</summary>
    /// <param name="key">The address to grant access to.</param>
    /// <param name="ttl">How long the URL stays valid. Keep it short.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The URL.</returns>
    /// <remarks>
    /// Authorisation is decided before this is called and never delegated to the backend: buckets
    /// stay private, permissions live in the application, and a change of provider therefore cannot
    /// take the security model with it.
    /// </remarks>
    Task<Uri> PresignReadAsync(ObjectKey key, TimeSpan ttl, CancellationToken ct = default);
}
