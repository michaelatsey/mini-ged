using Ged.Domain.Documents;
using Ged.Domain.Documents.Identifiers;
using MicroKit.Persistence.Abstractions;

namespace Ged.Adapters.Persistence.Repositories;

/// <summary>Loads and stores <see cref="Document"/> aggregates through EF Core.</summary>
/// <param name="context">The write-side context.</param>
/// <remarks>
/// <see langword="internal"/> so no caller can bypass <see cref="IDocumentRepository"/> by
/// referencing the implementation. An abstraction that can be sidestepped protects nothing.
/// </remarks>
internal sealed class EfDocumentRepository(GedDbContext context) : IDocumentRepository
{
    /// <inheritdoc />
    public async ValueTask<Document?> FindAsync(DocumentId id, CancellationToken ct = default) =>
        await context.Documents.SingleOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc />
    public async ValueTask AddAsync(Document aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        await context.Documents.AddAsync(aggregate, ct);
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(Document aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // Deliberately a no-op. The aggregate was loaded by this context, so change tracking has
        // already recorded every mutation. Calling Update here would mark every property modified
        // and write columns that never changed.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(Document aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // Documents are never physically removed: deletion is a state the aggregate carries, and
        // the blobs it referenced are examined later by the collector. Removing the row would
        // destroy the history that answers what happened to a document.
        throw new PersistenceException(
            "Documents are deleted logically. Call Document.SoftDelete instead.");
    }

    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken ct = default) =>
        await context.SaveChangesAsync(ct);
}
