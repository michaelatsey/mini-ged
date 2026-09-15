using Ged.Domain.Folders;
using Ged.Domain.Folders.Identifiers;
using Ged.Domain.Folders.ValueObjects;

namespace Ged.Features.Folders.RenameFolder;

/// <summary>Renames a folder.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="NewName">The new display name.</param>
/// <param name="By">The acting identity.</param>
public sealed record RenameFolderCommand(Guid FolderId, string NewName, string By);

/// <summary>Handles <see cref="RenameFolderCommand"/>.</summary>
/// <param name="folders">Loads the folder.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
/// <remarks>
/// Uniqueness among siblings is not checked here. A list loaded a moment earlier would look like a
/// guarantee while a concurrent insert slipped past it, so the database holds that constraint and
/// the violation arrives as a conflict.
/// </remarks>
public sealed class RenameFolderHandler(
    IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<bool>> HandleAsync(
        RenameFolderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var folder = await folders.FindAsync(FolderId.From(command.FolderId), ct);

        if (folder is null || folder.IsDeleted)
            return Outcome.NotFound<bool>("Folder");

        folder.Rename(new FolderName(command.NewName), clock.UtcNow, new Actor(command.By));

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<bool>(true);
    }
}
