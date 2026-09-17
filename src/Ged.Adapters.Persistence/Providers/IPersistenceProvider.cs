namespace Ged.Adapters.Persistence.Providers;

/// <summary>
/// The parts of persistence that genuinely differ between database engines.
/// </summary>
/// <remarks>
/// <para>
/// Everything that can be written once is written once: the context, the entity configurations,
/// the repositories and the outbox are provider-agnostic. This interface holds only what cannot be,
/// and each member exists because a real difference was found rather than anticipated.
/// </para>
/// <para>
/// The alternative — branching on <c>Database.IsNpgsql()</c> inside the shared configurations —
/// would drag every provider package into the shared assembly and make "which engine am I on"
/// a question asked in a dozen places instead of answered in one.
/// </para>
/// </remarks>
public interface IPersistenceProvider
{
    /// <summary>Gets the provider name, for diagnostics and for selecting migration scripts.</summary>
    string Name { get; }

    /// <summary>
    /// Adds the optimistic concurrency token to an aggregate.
    /// </summary>
    /// <param name="builder">The entity being configured.</param>
    /// <remarks>
    /// Deliberately a shadow property: concurrency is a persistence concern, and an aggregate that
    /// carried a <c>Version</c> member would be exposing the storage engine's bookkeeping as part
    /// of its business API.
    /// <para>
    /// The CLR type differs by engine, which is the reason this cannot live in shared code.
    /// PostgreSQL maps a <see langword="uint"/> onto the hidden <c>xmin</c> system column;
    /// SQL Server maps a <see langword="byte"/> array onto a <c>rowversion</c> column.
    /// </para>
    /// </remarks>
    void ConfigureConcurrencyToken(EntityTypeBuilder builder);

    /// <summary>Gets the column type for a JSON document.</summary>
    string JsonColumnType { get; }

    /// <summary>Gets the column type for an instant with offset.</summary>
    string InstantColumnType { get; }

    /// <summary>
    /// Gets the statement that claims a batch of pending outbox messages for this worker only.
    /// </summary>
    /// <remarks>
    /// Both engines can skip rows another transaction holds rather than blocking on them — the
    /// property that lets several dispatchers run without a distributed lock — but they spell it
    /// differently, and getting it wrong turns a scaled-out dispatcher into a queue of blocked
    /// workers or, worse, into duplicate deliveries.
    /// </remarks>
    string ClaimPendingOutboxSql { get; }

    /// <summary>Gets the statement that marks a set of outbox messages as delivered.</summary>
    string MarkOutboxProcessedSql { get; }

    /// <summary>
    /// Gets the recursive query returning a folder's ancestry, ordered from the root down.
    /// </summary>
    /// <remarks>
    /// PostgreSQL requires the <c>RECURSIVE</c> keyword on the common table expression; SQL Server
    /// rejects it. The depth cap in both variants is not decoration: a cycle introduced by a faulty
    /// migration would otherwise recurse until the connection dies.
    /// </remarks>
    string FolderAncestrySql { get; }
}
