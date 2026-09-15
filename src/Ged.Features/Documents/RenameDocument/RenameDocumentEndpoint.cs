namespace Ged.Features.Documents.RenameDocument;

/// <summary>Maps <c>PATCH /documents/{id}/name</c>.</summary>
internal static class RenameDocumentEndpoint
{
    /// <summary>The body of a rename request.</summary>
    /// <param name="Name">The new display name.</param>
    public sealed record Body(string Name);

    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapPatch("/{id:guid}/name", HandleAsync)
            .WithName("RenameDocument")
            .WithSummary("Renames a document.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, Body body, RenameDocumentHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new RenameDocumentCommand(id, body.Name, http.User.Identity?.Name ?? "anonymous"), ct);

        return outcome.ToResult(_ => Results.NoContent());
    }
}
