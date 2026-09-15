namespace Ged.Features.Documents.UploadDocument;

/// <summary>Maps <c>POST /documents</c>.</summary>
internal static class UploadDocumentEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapPost("/", HandleAsync)
            .WithName("UploadDocument")
            .WithSummary("Creates a document from an uploaded file.")
            .DisableAntiforgery()
            .Produces<UploadDocumentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        IFormFile file,
        Guid folderId,
        string? docType,
        UploadDocumentHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        await using var content = file.OpenReadStream();

        var outcome = await handler.HandleAsync(
            new UploadDocumentCommand(
                folderId, file.FileName, docType, content, http.User.Identity?.Name ?? "anonymous"),
            ct);

        return outcome.ToResult(response =>
            Results.Created($"/documents/{response.DocumentId}", response));
    }
}
