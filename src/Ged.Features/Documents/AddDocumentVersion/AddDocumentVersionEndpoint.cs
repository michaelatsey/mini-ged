namespace Ged.Features.Documents.AddDocumentVersion;

/// <summary>Maps <c>POST /documents/{id}/versions</c>.</summary>
internal static class AddDocumentVersionEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapPost("/{id:guid}/versions", HandleAsync)
            .WithName("AddDocumentVersion")
            .WithSummary("Appends a new content version to a document.")
            .DisableAntiforgery()
            .Produces<AddDocumentVersionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id,
        IFormFile file,
        string? comment,
        AddDocumentVersionHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        await using var content = file.OpenReadStream();

        var outcome = await handler.HandleAsync(
            new AddDocumentVersionCommand(
                id, file.FileName, comment, content, http.User.Identity?.Name ?? "anonymous"),
            ct);

        return outcome.ToResult(Results.Ok);
    }
}
