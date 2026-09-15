namespace Ged.Features.Documents.GetDocumentById;

/// <summary>Maps <c>GET /documents/{id}</c>.</summary>
internal static class GetDocumentByIdEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetDocumentById")
            .WithSummary("Reads one document.")
            .Produces<DocumentDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        Guid id, GetDocumentByIdHandler handler, CancellationToken ct)
    {
        var outcome = await handler.HandleAsync(id, ct);

        return outcome.ToResult(Results.Ok);
    }
}
