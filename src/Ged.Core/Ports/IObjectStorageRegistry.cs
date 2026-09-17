using Ged.Domain.Blobs;

namespace Ged.Core.Ports;

/// <summary>Resolves a provider name to the adapter that speaks for it.</summary>
/// <remarks>
/// A blob can live in several backends at once — that is what makes a provider migration a data
/// operation rather than a code change — so the application must be able to reach any of them by
/// name, not just the one it writes to today.
/// </remarks>
public interface IObjectStorageRegistry
{
    /// <summary>Gets the backend new content is written to.</summary>
    IObjectStorage Primary { get; }

    /// <summary>Gets the adapter for a provider.</summary>
    /// <param name="provider">The provider name stored on the location.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no adapter is registered for that provider — which means a location in the
    /// database points at a backend this deployment cannot reach.
    /// </exception>
    IObjectStorage Resolve(StorageProvider provider);

    /// <summary>Attempts to get the adapter for a provider.</summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="storage">The adapter, when one is registered.</param>
    /// <returns>True when an adapter is registered.</returns>
    bool TryGet(StorageProvider provider, out IObjectStorage? storage);
}
