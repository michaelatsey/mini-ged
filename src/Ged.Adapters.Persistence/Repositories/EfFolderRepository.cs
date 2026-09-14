using Dapper;
using Ged.Core.Ports;
using Ged.Domain.Folders;
using Ged.Domain.Folders.Identifiers;
using MicroKit.Persistence.Abstractions;

namespace Ged.Adapters.Persistence.Repositories;

/// <summary>Loads and stores <see cref="Folder"/> aggregates through EF Core.</summary>
/// <param name="context">The write-side context.</param>
/// <param name="connections">Opens connections for the ancestry query.</param>
internal sealed class EfFolderRepository(GedDbContext context, IDbConnectionFactory connections)
    : IFolderRepository
{
    // A recursive CTE walking parent links upward, then reversed so the caller receives the chain
    // root-first — the order FolderAncestry expects, and the order a breadcrumb reads in.
    //
    // Depth is capped in the query itself. A cycle introduced by a bad migration would otherwise
    // make this recurse forever, and a hung connection is a worse failure than a rejected request.
    private const string AncestrySql = """
        WITH RECURSIVE chain AS (
            SELECT id, parent_id, 1 AS depth
            FROM   folder
            WHERE  id = @FolderId

            UNION ALL

            SELECT f.id, f.parent_id, c.depth + 1
            FROM   folder f
            JOIN   chain  c ON f.id = c.parent_id
            WHERE  c.depth < @MaxDepth
        )
        SELECT id
        FROM   chain
        ORDER  BY depth DESC;
        """;

    /// <inheritdoc />
    public async ValueTask<Folder?> FindAsync(FolderId id, CancellationToken ct = default) =>
        await context.Folders.SingleOrDefaultAsync(f => f.Id == id, ct);

    /// <inheritdoc />
    public async ValueTask<FolderAncestry?> GetAncestryAsync(
        FolderId id, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            AncestrySql,
            new { FolderId = id.Value, MaxDepth = FolderAncestry.MaxDepth + 1 },
            cancellationToken: ct));

        var chain = rows.Select(FolderId.From).ToArray();

        return chain.Length == 0 ? null : FolderAncestry.Of(chain);
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(Folder aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        await context.Folders.AddAsync(aggregate, ct);
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(Folder aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(Folder aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        throw new PersistenceException(
            "Folders are deleted logically. Call Folder.SoftDelete instead.");
    }

    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken ct = default) =>
        await context.SaveChangesAsync(ct);
}
