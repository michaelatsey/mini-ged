namespace Ged.Features.Folders.RenameFolder;

/// <summary>Maps <c>PATCH /folders/{id}/name</c>.</summary>
internal static class RenameFolderEndpoint
{
    /// <summary>The body of a rename request.</summary>
    /// <param name="Name">The new display name.</param>
    public sealed record Body(string Name);

    /// <summary>Registers the route.</summary>
    /// <param name="folders">The folders route group.</param>
    public static void Map(RouteGroupBuilder folders) =>
        folders.MapPatch("/{id:guid}/name", HandleAsync)
            .WithName("RenameFolder")
            .WithSummary("Renames a folder.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, Body body, RenameFolderHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new RenameFolderCommand(id, body.Name, http.User.Identity?.Name ?? "anonymous"), ct);

        return outcome.ToResult(_ => Results.NoContent());
    }
}
