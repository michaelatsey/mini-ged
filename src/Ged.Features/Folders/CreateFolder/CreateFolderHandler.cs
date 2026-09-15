using Ged.Domain.Folders;
using Ged.Domain.Folders.Identifiers;
using Ged.Domain.Folders.ValueObjects;

namespace Ged.Features.Folders.CreateFolder;

/// <summary>Creates a folder, at the root or beneath another.</summary>
/// <param name="ParentId">The parent, or null to create a root.</param>
/// <param name="Name">The display name.</param>
/// <param name="FolderType">The classification, or null for unknown.</param>
/// <param name="By">The acting identity.</param>
public sealed record CreateFolderCommand(Guid? ParentId, string Name, string? FolderType, string By);

/// <summary>Handles <see cref="CreateFolderCommand"/>.</summary>
/// <param name="folders">Loads the parent's ancestry and stages the new folder.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
/// <remarks>
/// The parent's ancestry is loaded here and handed to the aggregate. A folder knows its parent, not
/// its ancestors, so the depth rule cannot be evaluated without that chain — and having the
/// aggregate fetch it would put a database call inside the domain.
/// </remarks>
public sealed class CreateFolderHandler(
    IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome<Guid>> HandleAsync(
        CreateFolderCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actor = new Actor(command.By);
        var name = new FolderName(command.Name);
        var type = new FolderType(command.FolderType ?? Ged.Domain.Folders.ValueObjects.FolderType.Unknown.Code);
        var now = clock.UtcNow;

        Folder folder;

        if (command.ParentId is { } parentId)
        {
            var ancestry = await folders.GetAncestryAsync(FolderId.From(parentId), ct);

            if (ancestry is null)
                return Outcome.NotFound<Guid>("Parent folder");

            folder = Folder.CreateChild(ancestry, name, type, now, actor);
        }
        else
        {
            folder = Folder.CreateRoot(name, type, now, actor);
        }

        await folders.AddAsync(folder, ct);
        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<Guid>(folder.Id.Value);
    }
}
