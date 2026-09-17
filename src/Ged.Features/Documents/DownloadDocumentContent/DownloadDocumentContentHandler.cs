using Ged.Domain.Blobs;
using Ged.Domain.Documents;

namespace Ged.Features.Documents.DownloadDocumentContent;

/// <summary>Opens a document's content for download.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="VersionId">A specific version, or null for the one in force.</param>
public sealed record DownloadDocumentContentQuery(Guid DocumentId, Guid? VersionId);

/// <summary>Content ready to be streamed to a client.</summary>
/// <param name="Content">The bytes and how they were obtained.</param>
/// <param name="FileName">The name to present.</param>
/// <param name="MimeType">The media type to declare.</param>
/// <param name="SizeBytes">The size, in bytes.</param>
public sealed record DocumentContent(
    BlobContent Content, string FileName, string MimeType, long SizeBytes);

/// <summary>Handles <see cref="DownloadDocumentContentQuery"/>.</summary>
/// <param name="documents">Loads the document and its versions.</param>
/// <param name="blobs">Loads the blob and its locations.</param>
/// <param name="resolver">Picks a location that can serve the content.</param>
/// <remarks>
/// <para>
/// This is one of the few reads that goes through the aggregates rather than SQL, and for a reason:
/// the answer is not a projection but a decision about which of several storage locations should
/// serve the bytes. That decision lives in <see cref="IBlobLocationResolver"/>, and it needs the
/// blob's locations in their read order.
/// </para>
/// <para>
/// The handler never names a provider. That is what lets content move between backends while
/// downloads keep working.
/// </para>
/// </remarks>
public sealed class DownloadDocumentContentHandler(
    IDocumentRepository documents, IBlobRepository blobs, IBlobLocationResolver resolver)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="query">The query.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome. The caller disposes the content.</returns>
    public async Task<Outcome<DocumentContent>> HandleAsync(
        DownloadDocumentContentQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var document = await documents.FindAsync(DocumentId.From(query.DocumentId), ct);

        if (document is null || document.IsDeleted)
            return Outcome.NotFound<DocumentContent>("Document");

        var version = query.VersionId is { } requested
            ? document.Versions.FirstOrDefault(v => v.Id.Value == requested)
            : document.CurrentVersion;

        if (version is null)
            return Outcome.NotFound<DocumentContent>("Version");

        var blob = await blobs.FindAsync(version.BlobId, ct);

        if (blob is null || blob.IsPurged)
            return Outcome.Fail<DocumentContent>(
                "CONFLICT", "The content of this version is no longer stored.");

        var content = await resolver.OpenAsync(blob, ct);

        return Outcome.Ok<DocumentContent>(new DocumentContent(
            content, version.FileName.Value, version.MimeType.Value, version.SizeBytes));
    }
}
