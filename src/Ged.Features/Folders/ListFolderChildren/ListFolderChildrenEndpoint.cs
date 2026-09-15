namespace Ged.Features.Folders.ListFolderChildren;

/// <summary>Maps <c>GET /folders</c>.</summary>
internal static class ListFolderChildrenEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="folders">The folders route group.</param>
    public static void Map(RouteGroupBuilder folders) =>
        folders.MapGet("/", HandleAsync)
            .WithName("ListFolderChildren")
            .WithSummary("Lists the direct children of a folder, or the roots when none is given.")
            .Produces<IReadOnlyList<FolderListItem>>();

    private static async Task<IResult> HandleAsync(
        ListFolderChildrenHandler handler, CancellationToken ct, Guid? parentId = null) =>
        Results.Ok(await handler.HandleAsync(parentId, ct));
}
