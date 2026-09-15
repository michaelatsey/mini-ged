using Ged.Core.Ports;
using Ged.Domain.Blobs.ValueObjects;

namespace Ged.Adapters.Storage;

/// <summary>Resolves provider names to the adapters registered in this deployment.</summary>
/// <param name="storages">Every registered adapter.</param>
/// <param name="primaryProvider">The provider new content is written to.</param>
public sealed class ObjectStorageRegistry(
    IEnumerable<IObjectStorage> storages,
    StorageProvider primaryProvider) : IObjectStorageRegistry
{
    private readonly Dictionary<string, IObjectStorage> _byName =
        storages.ToDictionary(s => s.Provider.Name, StringComparer.Ordinal);

    /// <inheritdoc />
    public IObjectStorage Primary =>
        _byName.TryGetValue(primaryProvider.Name, out var storage)
            ? storage
            : throw new InvalidOperationException(
                $"No storage adapter is registered for the configured primary provider '{primaryProvider}'.");

    /// <inheritdoc />
    public IObjectStorage Resolve(StorageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return _byName.TryGetValue(provider.Name, out var storage)
            ? storage
            : throw new InvalidOperationException(
                $"No storage adapter is registered for provider '{provider}'. A blob location " +
                "points at a backend this deployment cannot reach.");
    }

    /// <inheritdoc />
    public bool TryGet(StorageProvider provider, out IObjectStorage? storage)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return _byName.TryGetValue(provider.Name, out storage);
    }
}
