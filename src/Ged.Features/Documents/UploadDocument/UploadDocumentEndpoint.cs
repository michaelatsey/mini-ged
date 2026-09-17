using Ged.Features.Common.FileTypes;

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
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .RequireRateLimiting(GedPolicies.Content)
            .WithRequestTimeout(GedPolicies.Content);

    private static async Task<IResult> HandleAsync(
        IFormFile file,
        Guid folderId,
        string? docType,
        UploadDocumentHandler handler,
        IFileTypeInspector fileTypes,
        HttpContext http,
        CancellationToken ct)
    {
        // Refused here, before a single byte is read from the request body. The content check is
        // authoritative but needs the file staged to disk first; there is no reason to buffer 200 MB
        // of an executable to discover its extension was never acceptable.
        var byName = fileTypes.CheckName(file.FileName, file.ContentType, docType);

        if (!byName.Accepted)
        {
            return Outcome.Fail<object>("UNSUPPORTED_MEDIA_TYPE", byName.Message)
                .ToResult(_ => Results.Empty);
        }

        await using var content = file.OpenReadStream();

        var outcome = await handler.HandleAsync(
            new UploadDocumentCommand(
                folderId, file.FileName, docType, content, http.User.Identity?.Name ?? "anonymous"),
            ct);

        return outcome.ToResult(response =>
            Results.Created($"/documents/{response.DocumentId}", response));
    }
}
