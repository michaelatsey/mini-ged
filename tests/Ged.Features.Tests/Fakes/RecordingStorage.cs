namespace Ged.Features.Tests.Fakes;

/// <summary>
/// An <see cref="IObjectStorage"/> that records every write and every removal instead of
/// performing one. Storage is the one place the system cannot roll back, so what reaches it — and
/// when — is what the tests assert.
/// </summary>
internal sealed class RecordingStorage(Journal journal) : IObjectStorage
{
    private readonly List<ObjectKey> _written = [];
    private readonly List<ObjectKey> _deleted = [];

    public IReadOnlyList<ObjectKey> Written => _written;

    public IReadOnlyList<ObjectKey> Deleted => _deleted;

    public StorageProvider Provider => StorageProvider.Beys;

    public string DefaultBucket => "ged";

    public ObjectKey KeyFor(string digest) => new(DefaultBucket, $"ged/{digest}");

    public Task PutAsync(
        ObjectKey key, Stream content, ObjectMetadata metadata, CancellationToken ct = default)
    {
        _written.Add(key);
        journal.Record($"put:{key.Key}");

        return Task.CompletedTask;
    }

    public Task DeleteAsync(ObjectKey key, CancellationToken ct = default)
    {
        _deleted.Add(key);
        journal.Record($"delete:{key.Key}");

        return Task.CompletedTask;
    }

    public Task<Stream> OpenAsync(ObjectKey key, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<bool> ExistsAsync(ObjectKey key, CancellationToken ct = default) =>
        Task.FromResult(_written.Contains(key) && !_deleted.Contains(key));

    public Task CopyAsync(ObjectKey source, ObjectKey target, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<ObjectKey> ListAsync(CancellationToken ct = default) =>
        _written.ToAsyncEnumerable();
}

/// <summary>A registry with one backend, which is what a test deployment has.</summary>
internal sealed class SingleStorageRegistry(IObjectStorage storage) : IObjectStorageRegistry
{
    public IObjectStorage Primary => storage;

    public IObjectStorage Resolve(StorageProvider provider) =>
        provider == storage.Provider
            ? storage
            : throw new InvalidOperationException($"No adapter for '{provider}'.");

    public bool TryGet(StorageProvider provider, out IObjectStorage? found)
    {
        found = provider == storage.Provider ? storage : null;

        return found is not null;
    }
}

/// <summary>Turns a list into the async sequence <see cref="IObjectStorage.ListAsync"/> returns.</summary>
internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IReadOnlyList<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
