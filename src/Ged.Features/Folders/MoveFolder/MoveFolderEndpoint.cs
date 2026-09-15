namespace Ged.Features.Folders.MoveFolder;

/// <summary>Maps <c>PATCH /folders/{id}/parent</c>.</summary>
internal static class MoveFolderEndpoint
{
    /// <summary>The body of a move request.</summary>
    /// <param name="ParentId">The destination parent, or null to promote to the root.</param>
    public sealed record Body(Guid? ParentId);

    /// <summary>Registers the route.</summary>
    /// <param name="folders">The folders route group.</param>
    public static void Map(RouteGroupBuilder folders) =>
        folders.MapPatch("/{id:guid}/parent", HandleAsync)
            .WithName("MoveFolder")
            .WithSummary("Moves a folder, refusing a move into its own subtree.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, Body body, MoveFolderHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new MoveFolderCommand(id, body.ParentId, http.User.Identity?.Name ?? "anonymous"), ct);

        return outcome.ToResult(_ => Results.NoContent());
    }
}
