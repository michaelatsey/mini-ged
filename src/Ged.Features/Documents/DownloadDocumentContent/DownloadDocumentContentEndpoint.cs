namespace Ged.Features.Documents.DownloadDocumentContent;

/// <summary>Maps <c>GET /documents/{id}/content</c>.</summary>
internal static class DownloadDocumentContentEndpoint
{
    /// <summary>Registers the route.</summary>
    /// <param name="documents">The documents route group.</param>
    public static void Map(RouteGroupBuilder documents) =>
        documents.MapGet("/{id:guid}/content", HandleAsync)
            .WithName("DownloadDocumentContent")
            .WithSummary("Streams the content of a document.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting(GedPolicies.Content)
            .WithRequestTimeout(GedPolicies.Content);

    private static async Task<IResult> HandleAsync(
        Guid id,
        DownloadDocumentContentHandler handler,
        HttpContext http,
        CancellationToken ct,
        Guid? versionId = null)
    {
        var outcome = await handler.HandleAsync(
            new DownloadDocumentContentQuery(id, versionId), ct);

        if (!outcome.Succeeded)
            return outcome.ToResult(_ => Results.Empty);

        var content = outcome.Value!;

        // A location had to be skipped. Routine while content is being copied between backends,
        // worth repairing otherwise — so it is surfaced as a header rather than silently dropped.
        if (content.Content.ServedFromFallback)
            http.Response.Headers.Append("X-Ged-Served-From-Fallback", "true");

        // The stream is handed to the response pipeline, which disposes it once written.
        return Results.File(
            content.Content.Stream,
            contentType: content.MimeType,
            fileDownloadName: content.FileName,
            enableRangeProcessing: true);
    }
}
