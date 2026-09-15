namespace Ged.Features.Documents.MoveDocument;

/// <summary>Maps <c>PATCH /documents/{id}/folder</c>.</summary>
internal static class MoveDocumentEndpoint
{
    /// <summary>The body of a move request.</summary>
    /// <param name="FolderId">The destination folder.</param>
    public sealed record Body(Guid FolderId);

    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapPatch("/{id:guid}/folder", HandleAsync)
            .WithName("MoveDocument")
            .WithSummary("Moves a document to another folder.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, Body body, MoveDocumentHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new MoveDocumentCommand(id, body.FolderId, http.User.Identity?.Name ?? "anonymous"), ct);

        return outcome.ToResult(_ => Results.NoContent());
    }
}
