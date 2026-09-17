using Ged.Features.Common.FileTypes;

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
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .RequireRateLimiting(GedPolicies.Content)
            .WithRequestTimeout(GedPolicies.Content);

    private static async Task<IResult> HandleAsync(
        Guid id,
        IFormFile file,
        string? comment,
        AddDocumentVersionHandler handler,
        IFileTypeInspector fileTypes,
        HttpContext http,
        CancellationToken ct)
    {
        // The document's classification is not known here without a read, so the cheap check runs
        // against the global allowlist only. The handler narrows it once the document is loaded.
        var byName = fileTypes.CheckName(file.FileName, file.ContentType, docType: null);

        if (!byName.Accepted)
        {
            return Outcome.Fail<object>("UNSUPPORTED_MEDIA_TYPE", byName.Message)
                .ToResult(_ => Results.Empty);
        }

        await using var content = file.OpenReadStream();

        var outcome = await handler.HandleAsync(
            new AddDocumentVersionCommand(
                id, file.FileName, comment, content, http.User.Identity?.Name ?? "anonymous"),
            ct);

        return outcome.ToResult(Results.Ok);
    }
}
