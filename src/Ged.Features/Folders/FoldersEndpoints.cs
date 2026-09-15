using Ged.Features.Folders.CreateFolder;
using Ged.Features.Folders.DeleteFolder;
using Ged.Features.Folders.GetFolderById;
using Ged.Features.Folders.ListFolderChildren;
using Ged.Features.Folders.MoveFolder;
using Ged.Features.Folders.RenameFolder;

namespace Ged.Features.Folders;

/// <summary>Registers every folder route, explicitly.</summary>
internal static class FoldersEndpoints
{
    /// <summary>Maps the folder routes.</summary>
    /// <param name="version">The versioned route group.</param>
    public static void Map(RouteGroupBuilder version)
    {
        var folders = version.MapGroup("/folders").WithTags("Folders");

        CreateFolderEndpoint.Map(folders);
        RenameFolderEndpoint.Map(folders);
        MoveFolderEndpoint.Map(folders);
        DeleteFolderEndpoint.Map(folders);
        GetFolderByIdEndpoint.Map(folders);
        ListFolderChildrenEndpoint.Map(folders);
    }
}
