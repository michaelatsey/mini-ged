using Dapper;
using Ged.Adapters.Persistence.Providers;
using Ged.Core.Ports;
using Ged.Domain.Folders;
using Ged.Domain.Folders.Identifiers;
using MicroKit.Persistence.Abstractions;

namespace Ged.Adapters.Persistence.Repositories;

/// <summary>Loads and stores <see cref="Folder"/> aggregates through EF Core.</summary>
/// <param name="context">The write-side context.</param>
/// <param name="connections">Opens connections for the ancestry query.</param>
internal sealed class EfFolderRepository(
    GedDbContext context,
    IDbConnectionFactory connections,
    IPersistenceProvider provider) : IFolderRepository
{
    /// <inheritdoc />
    public async ValueTask<Folder?> FindAsync(FolderId id, CancellationToken ct = default) =>
        await context.Folders.SingleOrDefaultAsync(f => f.Id == id, ct);

    /// <inheritdoc />
    public async ValueTask<FolderAncestry?> GetAncestryAsync(
        FolderId id, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            provider.FolderAncestrySql,
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
