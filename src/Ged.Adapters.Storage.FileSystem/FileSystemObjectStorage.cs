using System.Runtime.CompilerServices;
using Ged.Core.Ports;
using Ged.Domain.Blobs.ValueObjects;

namespace Ged.Adapters.Storage.FileSystem;

/// <summary>Stores content as files on a local or mounted filesystem.</summary>
/// <param name="options">Where objects live.</param>
/// <remarks>
/// <para>
/// A real adapter, not a mock. It lets the whole system run — upload, download, migration between
/// providers, collection and purge — with nothing installed: no MinIO, no Beys, no cloud account.
/// That matters beyond convenience, because the second adapter is what proves the abstraction is
/// one. An <see cref="IObjectStorage"/> with a single implementation is an untested hypothesis.
/// </para>
/// <para>
/// Content is addressed by its digest and sharded two levels deep — <c>a3/f9/a3f9c1…</c> — because
/// a directory holding a million sibling entries degrades badly on most filesystems, and the first
/// bytes of a SHA-256 are uniformly distributed, so the shards stay even for free.
/// </para>
/// <para>
/// Writes go to a temporary file and are then moved into place. A move within one volume is atomic,
/// so a crash mid-write leaves a stray temporary file rather than a truncated object that the
/// database believes is complete — and a stray file is something reconciliation can clean up.
/// </para>
/// </remarks>
public sealed class FileSystemObjectStorage(FileSystemStorageOptions options) : IObjectStorage
{
    private readonly FileSystemStorageOptions _options = options;

    /// <inheritdoc />
    public StorageProvider Provider => field ??= new StorageProvider(_options.ProviderName);

    /// <inheritdoc />
    public string DefaultBucket => _options.DefaultBucket;

    /// <inheritdoc />
    public ObjectKey KeyFor(string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        if (digest.Length < 4)
            throw new ArgumentException("A content digest is too short to shard.", nameof(digest));

        return new ObjectKey(DefaultBucket, $"{digest[..2]}/{digest[2..4]}/{digest}");
    }

    /// <inheritdoc />
    public async Task PutAsync(
        ObjectKey key, Stream content, ObjectMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);

        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Content is immutable and addressed by its digest, so an object that already exists holds
        // exactly these bytes. Rewriting it would be work with no possible effect.
        if (File.Exists(path))
            return;

        var temporary = $"{path}.{Guid.CreateVersion7():N}.tmp";

        try
        {
            await using (var destination = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(destination, ct);
                await destination.FlushAsync(ct);
            }

            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another writer won the race with identical content. Nothing to repair.
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(ObjectKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var path = PathFor(key);

        if (!File.Exists(path))
            throw new ObjectNotFoundException(Provider, key);

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(ObjectKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        return Task.FromResult(File.Exists(PathFor(key)));
    }

    /// <inheritdoc />
    public Task CopyAsync(ObjectKey source, ObjectKey target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var from = PathFor(source);

        if (!File.Exists(from))
            throw new ObjectNotFoundException(Provider, source);

        var to = PathFor(target);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);

        if (!File.Exists(to))
            File.Copy(from, to);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(ObjectKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var path = PathFor(key);

        // Deleting something already gone is the outcome the caller wanted. A collector that threw
        // here would stall on its own previous success.
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ObjectKey> ListAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var bucketRoot = Path.Combine(_options.RootPath, DefaultBucket);

        if (!Directory.Exists(bucketRoot))
            yield break;

        foreach (var file in Directory.EnumerateFiles(bucketRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            // Temporary files belong to a write in flight, not to the object store.
            if (file.EndsWith(".tmp", StringComparison.Ordinal))
                continue;

            var relative = Path.GetRelativePath(bucketRoot, file).Replace('\\', '/');

            yield return new ObjectKey(DefaultBucket, relative);
        }

        await Task.CompletedTask;
    }

    private string PathFor(ObjectKey key)
    {
        var root = Path.GetFullPath(_options.RootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, key.Bucket, key.Key));

        // Keys come from the database, and a corrupted or hostile row must not be able to reach
        // outside the storage root. The domain already rejects separators in names; this is the
        // second line, at the only place that turns a key into a path.
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Object key '{key}' escapes the storage root.");

        return candidate;
    }
}
