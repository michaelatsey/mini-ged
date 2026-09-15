namespace Ged.Features.Folders.DeleteFolder;

/// <summary>Maps <c>DELETE /folders/{id}</c>.</summary>
internal static class DeleteFolderEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="folders">The folders route group.</param>
    public static void Map(RouteGroupBuilder folders) =>
        folders.MapDelete("/{id:guid}", HandleAsync)
            .WithName("DeleteFolder")
            .WithSummary("Logically deletes a folder, refusing if it still holds content.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id, DeleteFolderHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new DeleteFolderCommand(id, http.User.Identity?.Name ?? "anonymous"), ct);

        return outcome.ToResult(_ => Results.NoContent());
    }
}
