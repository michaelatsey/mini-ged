namespace Ged.Features.Documents.ListDocumentsByFolder;

/// <summary>Maps <c>GET /documents</c>.</summary>
internal static class ListDocumentsByFolderEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapGet("/", HandleAsync)
            .WithName("ListDocumentsByFolder")
            .WithSummary("Lists the documents of a folder.")
            .Produces<Page<DocumentListItem>>();

    private static async Task<IResult> HandleAsync(
        Guid folderId,
        ListDocumentsByFolderHandler handler,
        CancellationToken ct,
        int skip = 0,
        int take = 50) =>
        Results.Ok(await handler.HandleAsync(folderId, skip, take, ct));
}
