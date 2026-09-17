using Ged.Domain.Folders;

namespace Ged.Features.Folders.MoveFolder;

/// <summary>Moves a folder beneath another, or promotes it to the root.</summary>
/// <param name="FolderId">The folder to move.</param>
/// <param name="TargetParentId">The destination parent, or null to promote to the root.</param>
/// <param name="By">The acting identity.</param>
public sealed record MoveFolderCommand(Guid FolderId, Guid? TargetParentId, string By);

/// <summary>Handles <see cref="MoveFolderCommand"/>.</summary>
/// <param name="folders">Loads the folder and the destination's ancestry.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
/// <remarks>
/// The destination's ancestry is what lets the aggregate refuse a move into its own subtree — the
/// cycle that would silently detach a whole branch from the hierarchy. The handler loads the chain;
/// the decision stays in the domain.
/// </remarks>
public sealed class MoveFolderHandler(
    IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<bool>> HandleAsync(
        MoveFolderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var folder = await folders.FindAsync(FolderId.From(command.FolderId), ct);

        if (folder is null || folder.IsDeleted)
            return Outcome.NotFound<bool>("Folder");

        var actor = new Actor(command.By);
        var now = clock.UtcNow;

        if (command.TargetParentId is not { } targetId)
        {
            folder.MoveToRoot(now, actor);
        }
        else
        {
            var ancestry = await folders.GetAncestryAsync(FolderId.From(targetId), ct);

            if (ancestry is null)
                return Outcome.NotFound<bool>("Target folder");

            folder.MoveTo(ancestry, now, actor);
        }

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<bool>(true);
    }
}
