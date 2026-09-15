namespace Ged.Features.Folders.GetFolderById;

/// <summary>Maps <c>GET /folders/{id}</c>.</summary>
internal static class GetFolderByIdEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="folders">The folders route group.</param>
    public static void Map(RouteGroupBuilder folders) =>
        folders.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetFolderById")
            .WithSummary("Reads one folder, with its breadcrumb.")
            .Produces<FolderDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, GetFolderByIdHandler handler, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(id, ct);

        return outcome.ToResult(Results.Ok);
    }
}
