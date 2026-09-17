namespace Ged.Domain.Folders;

/// <summary>Loads and stores <see cref="Folder"/> aggregates.</summary>
/// <remarks>
/// <see cref="GetAncestryAsync"/> reads, but it belongs here rather than on the query side: it
/// returns identities that feed a domain rule, not a projection for a screen. The dividing line
/// for this contract is whether the result will be used to decide a mutation, not whether the
/// operation happens to read.
/// </remarks>
public interface IFolderRepository : IRepository<Folder>
{
    /// <summary>Loads a folder aggregate by identity.</summary>
    /// <param name="id">The folder identifier.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The folder, or null when no such folder exists.</returns>
    ValueTask<Folder?> FindAsync(FolderId id, CancellationToken ct = default);

    /// <summary>Loads the ancestry chain of a folder, from the root down to the folder itself.</summary>
    /// <param name="id">The folder identifier.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The ancestry, or null when no such folder exists.</returns>
    ValueTask<FolderAncestry?> GetAncestryAsync(FolderId id, CancellationToken ct = default);
}
