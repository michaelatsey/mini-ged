namespace Ged.Features.Folders.CreateFolder;

/// <summary>Maps <c>POST /folders</c>.</summary>
internal static class CreateFolderEndpoint
{
    /// <summary>The body of a create request.</summary>
    /// <param name="ParentId">The parent, or null to create a root.</param>
    /// <param name="Name">The display name.</param>
    /// <param name="FolderType">The classification, or null for unknown.</param>
    public sealed record Body(Guid? ParentId, string Name, string? FolderType);

    /// <summary>Registers the route.</summary>
    /// <param name="folders">The folders route group.</param>
    public static void Map(RouteGroupBuilder folders) =>
        folders.MapPost("/", HandleAsync)
            .WithName("CreateFolder")
            .WithSummary("Creates a folder, at the root or beneath another.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Body body, CreateFolderHandler handler, HttpContext http, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(
            new CreateFolderCommand(
                body.ParentId, body.Name, body.FolderType,
                http.User.Identity?.Name ?? "anonymous"),
            ct);

        return outcome.ToResult(id => Results.Created($"/folders/{id}", id));
    }
}
