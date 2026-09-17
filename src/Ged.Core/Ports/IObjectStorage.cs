
using Ged.Domain.Blobs;

namespace Ged.Core.Ports;

/// <summary>Reads and writes content in one storage backend.</summary>
/// <remarks>
/// <para>
/// Deliberately poor. The narrower this contract is, the easier it is to put an unforeseen backend
/// behind it — and an abstraction that exposes the capabilities of its richest implementation
/// abstracts nothing. Presigned URLs, bucket policies, storage classes and server-side versioning
/// are all absent for that reason; where one of them matters, it arrives as a separate capability
/// interface the caller tests for.
/// </para>
/// <para>
/// The port never composes a key. Keys are produced by the adapter and stored by the domain,
/// because a naming convention is a property of the backend: the moment the application builds
/// keys, that convention leaks everywhere and can no longer be changed.
/// </para>
/// </remarks>
public interface IObjectStorage
{
    /// <summary>Gets the provider this adapter speaks for.</summary>
    StorageProvider Provider { get; }

    /// <summary>Gets the container new objects are written to.</summary>
    string DefaultBucket { get; }

    /// <summary>Builds the address this adapter would use for the given content.</summary>
    /// <param name="digest">The lowercase hexadecimal SHA-256 of the content.</param>
    /// <returns>The address, to be stored by the domain and passed back on every later call.</returns>
    ObjectKey KeyFor(string digest);

    /// <summary>Writes content at the given address.</summary>
    /// <param name="key">The address to write to.</param>
    /// <param name="content">The content. Read once, forward-only.</param>
    /// <param name="metadata">Size and media type, when known.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    Task PutAsync(ObjectKey key, Stream content, ObjectMetadata metadata, CancellationToken ct = default);

    /// <summary>Opens content for reading.</summary>
    /// <param name="key">The address to read from.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A readable stream the caller is responsible for disposing.</returns>
    /// <exception cref="ObjectNotFoundException">Thrown when nothing exists at that address.</exception>
    Task<Stream> OpenAsync(ObjectKey key, CancellationToken ct = default);

    /// <summary>Determines whether content exists at the given address.</summary>
    /// <param name="key">The address to test.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>True when an object exists there.</returns>
    Task<bool> ExistsAsync(ObjectKey key, CancellationToken ct = default);

    /// <summary>Copies content from one address to another within this backend.</summary>
    /// <param name="source">The address to copy from.</param>
    /// <param name="target">The address to copy to.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <remarks>
    /// Present because most backends can copy server-side, without the bytes travelling through the
    /// application. An adapter that cannot does the read-and-write itself; the caller never has to
    /// know which is happening.
    /// </remarks>
    Task CopyAsync(ObjectKey source, ObjectKey target, CancellationToken ct = default);

    /// <summary>Removes content at the given address.</summary>
    /// <param name="key">The address to remove.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <remarks>
    /// Called only by the collector, after a retention window and a re-check. No business operation
    /// reaches this method.
    /// </remarks>
    Task DeleteAsync(ObjectKey key, CancellationToken ct = default);

    /// <summary>Lists the addresses held by this backend, for reconciliation.</summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>Every address currently present.</returns>
    /// <remarks>
    /// Exists for the reconciliation job, which walks storage looking for objects the database does
    /// not know about — orphans born of a write that succeeded while its transaction failed.
    /// </remarks>
    IAsyncEnumerable<ObjectKey> ListAsync(CancellationToken ct = default);
}
