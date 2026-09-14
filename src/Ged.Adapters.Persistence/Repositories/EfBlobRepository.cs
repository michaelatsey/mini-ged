using Ged.Domain.Blobs;
using Ged.Domain.Blobs.Identifiers;
using MicroKit.Persistence.Abstractions;

namespace Ged.Adapters.Persistence.Repositories;

/// <summary>Loads and stores <see cref="Blob"/> aggregates through EF Core.</summary>
/// <param name="context">The write-side context.</param>
/// <remarks>
/// <see cref="FindAsync"/> is also the deduplication check on the upload path: content already
/// registered under a digest must not be written to storage again. That is why it belongs on the
/// write side — its result decides whether an upload happens.
/// </remarks>
internal sealed class EfBlobRepository(GedDbContext context) : IBlobRepository
{
    /// <inheritdoc />
    public async ValueTask<Blob?> FindAsync(BlobId id, CancellationToken ct = default) =>
        await context.Blobs.SingleOrDefaultAsync(b => b.Id == id, ct);

    /// <inheritdoc />
    public async ValueTask AddAsync(Blob aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        await context.Blobs.AddAsync(aggregate, ct);
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(Blob aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(Blob aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // A purged blob keeps its row. The digest, the size and the purge instant are what lets an
        // operator answer "what happened to this content" months later, and a deleted row answers
        // nothing.
        throw new PersistenceException(
            "Blobs are never removed. Call Blob.MarkPurged once storage has been cleared.");
    }

    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken ct = default) =>
        await context.SaveChangesAsync(ct);
}
