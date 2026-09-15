namespace Ged.Features.Documents.DeleteDocument;

/// <summary>Maps <c>DELETE /documents/{id}</c>.</summary>
internal static class DeleteDocumentEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteDocument")
            .WithSummary("Logically deletes a document. Content is never removed here.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, DeleteDocumentHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new DeleteDocumentCommand(id, http.User.Identity?.Name ?? "anonymous"), ct);

        return outcome.ToResult(_ => Results.NoContent());
    }
}
