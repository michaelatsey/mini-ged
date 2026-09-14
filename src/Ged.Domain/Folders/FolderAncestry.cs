using Ged.Domain.Folders.Identifiers;

namespace Ged.Domain.Folders;

/// <summary>
/// The ordered chain of folder identifiers from the root down to a given folder, inclusive.
/// </summary>
/// <remarks>
/// <para>
/// A parameter object, never persisted. It carries facts the aggregate cannot observe on its
/// own — a folder knows its parent, not its ancestors — so hierarchy rules can be evaluated
/// inside the domain without the aggregate performing any I/O. The application loads the chain
/// and hands it in; the domain decides what it means.
/// </para>
/// <para>
/// It deliberately does not implement <c>IValueObject</c>: structural equality over a chain has
/// no business meaning here, and the type exists only to travel into a method call.
/// </para>
/// </remarks>
public sealed class FolderAncestry
{
    /// <summary>The maximum number of levels a folder hierarchy may contain.</summary>
    public const int MaxDepth = 16;

    private readonly FolderId[] _chain;

    private FolderAncestry(FolderId[] chain) => _chain = chain;

    /// <summary>Gets the folder this ancestry describes — the last link in the chain.</summary>
    public FolderId Self => _chain[^1];

    /// <summary>Gets the number of levels from the root to <see cref="Self"/>, inclusive.</summary>
    public int Depth => _chain.Length;

    /// <summary>Gets the chain, ordered from the root down to <see cref="Self"/>.</summary>
    public IReadOnlyList<FolderId> Chain => _chain;

    /// <summary>Builds an ancestry from an ordered chain of identifiers.</summary>
    /// <param name="fromRootToSelf">
    /// The identifiers from the root folder down to the target folder, inclusive. A root
    /// folder's own ancestry is a single-element chain.
    /// </param>
    /// <returns>The corresponding <see cref="FolderAncestry"/>.</returns>
    /// <exception cref="DomainException">
    /// Thrown when the chain is empty or repeats an identifier, which would mean the stored
    /// hierarchy already holds a cycle.
    /// </exception>
    public static FolderAncestry Of(params FolderId[] fromRootToSelf)
    {
        ArgumentNullException.ThrowIfNull(fromRootToSelf);

        if (fromRootToSelf.Length == 0)
            throw new DomainException("A folder ancestry must contain at least the folder itself.");

        if (fromRootToSelf.Distinct().Count() != fromRootToSelf.Length)
            throw new DomainException("A folder ancestry cannot contain the same folder twice.");

        return new FolderAncestry([.. fromRootToSelf]);
    }

    /// <summary>Determines whether the supplied folder appears anywhere in this chain.</summary>
    /// <param name="folderId">The folder to look for.</param>
    /// <returns>True when the folder is an ancestor or is <see cref="Self"/>.</returns>
    public bool Contains(FolderId folderId) => Array.IndexOf(_chain, folderId) >= 0;
}
