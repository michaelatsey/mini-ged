using MicroKit.Persistence.Abstractions;

namespace Ged.Adapters.Persistence.Repositories;

/// <summary>Commits everything staged in the current context as one transaction.</summary>
/// <param name="context">The write-side context.</param>
/// <remarks>
/// <para>
/// Separate from the repositories on purpose. A handler that registers a blob and creates a
/// document touches two aggregates and must commit them together; if committing lived on the
/// repository, there would be no way to express that.
/// </para>
/// <para>
/// Database errors are translated into <see cref="PersistenceException"/> so callers are not forced
/// to reference the provider to catch them. The concurrency case is kept distinct: it is retryable,
/// which a generic failure is not.
/// </para>
/// </remarks>
public sealed class EfUnitOfWork(GedDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceException(
                "The aggregate was modified by another transaction. Reload it and retry.", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new PersistenceException("The changes could not be saved.", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clears the EF Core change tracker: every <c>Added</c>, <c>Modified</c>, and <c>Deleted</c>
    /// entry is detached and nothing is written. Purely in-memory — no I/O, no provider exception,
    /// nothing to translate into <see cref="PersistenceException"/>. It does not touch the ambient
    /// database transaction: the transaction's own commit or rollback still decides the fate of
    /// anything already flushed inside it, and rows committed before it began are unaffected.
    /// </remarks>
    public void DiscardChanges() => context.ChangeTracker.Clear();
}
