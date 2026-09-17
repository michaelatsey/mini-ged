namespace Ged.Domain.Blobs;

/// <summary>Loads and stores <see cref="Blob"/> aggregates.</summary>
/// <remarks>
/// <see cref="FindAsync"/> doubles as the deduplication check: content already registered under a
/// digest is content that must not be written again. That is why the lookup belongs on the write
/// side — its result decides whether an upload happens, not what a screen displays.
/// </remarks>
public interface IBlobRepository : IRepository<Blob>
{
    /// <summary>Loads a blob aggregate by digest, including all of its locations.</summary>
    /// <param name="id">The content digest.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The blob, or null when the content has never been registered.</returns>
    ValueTask<Blob?> FindAsync(BlobId id, CancellationToken ct = default);
}
