namespace Ged.Features.Tests.Fakes;

/// <summary>
/// An <see cref="IUnitOfWork"/> that records its commits and can fail one of them the way a real
/// commit fails when another transaction changed a row the change set carries.
/// </summary>
/// <param name="journal">Where the commit is recorded, relative to the storage calls.</param>
internal sealed class RecordingUnitOfWork(Journal journal) : IUnitOfWork
{
    /// <summary>Gets or sets which commit fails, counting from one. Zero means none of them.</summary>
    public int FailAtCommit { get; init; }

    /// <summary>Gets how many times a commit was attempted.</summary>
    public int Attempts { get; private set; }

    /// <summary>Gets how many times a commit succeeded.</summary>
    public int Commits { get; private set; }

    /// <summary>Gets how many times the pending change set was discarded.</summary>
    public int Discards { get; private set; }

    public ValueTask CommitAsync(CancellationToken ct = default)
    {
        Attempts++;

        if (Attempts == FailAtCommit)
        {
            journal.Record("commit-failed");

            throw new PersistenceException(
                "The aggregate was modified by another transaction. Reload it and retry.");
        }

        Commits++;
        journal.Record("commit");

        return ValueTask.CompletedTask;
    }

    public void DiscardChanges() => Discards++;
}
