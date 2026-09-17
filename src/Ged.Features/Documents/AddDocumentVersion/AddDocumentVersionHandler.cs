using Ged.Domain.Blobs;
using Ged.Features.Common.FileTypes;
using Ged.Domain.Documents;

namespace Ged.Features.Documents.AddDocumentVersion;

/// <summary>Appends a new content version to an existing document.</summary>
/// <param name="DocumentId">The document.</param>
/// <param name="FileName">The file name as supplied by the client.</param>
/// <param name="Comment">An optional note describing the change.</param>
/// <param name="Content">The bytes. Read once, forward-only.</param>
/// <param name="By">The acting identity.</param>
public sealed record AddDocumentVersionCommand(
    Guid DocumentId, string FileName, string? Comment, Stream Content, string By);

/// <summary>What the caller gets back after appending a version.</summary>
/// <param name="VersionId">The new version.</param>
/// <param name="VersionNumber">Its ordinal.</param>
/// <param name="BlobId">The digest of the new content.</param>
public sealed record AddDocumentVersionResponse(Guid VersionId, int VersionNumber, string BlobId);

/// <summary>Handles <see cref="AddDocumentVersionCommand"/>.</summary>
/// <param name="documents">Loads the document.</param>
/// <param name="blobs">Finds or registers the content.</param>
/// <param name="storages">Supplies the backend content is written to.</param>
/// <param name="fileTypes">Decides whether the upload may be stored, and what it actually is.</param>
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
public sealed class AddDocumentVersionHandler(
    IDocumentRepository documents,
    IBlobRepository blobs,
    IObjectStorageRegistry storages,
    IFileTypeInspector fileTypes,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Appending never overwrites: the previous version keeps pointing at its own content, which is
    /// what lets several documents share a digest without one of them being able to alter what the
    /// others see. Re-uploading identical bytes is refused by the domain rather than recorded as a
    /// change.
    /// </remarks>
    public async Task<Outcome<AddDocumentVersionResponse>> HandleAsync(
        AddDocumentVersionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = await documents.FindAsync(DocumentId.From(command.DocumentId), ct);

        if (document is null || document.IsDeleted)
            return Outcome.NotFound<AddDocumentVersionResponse>("Document");

        var actor = new Actor(command.By);
        var name = new DocumentName(command.FileName);
        var now = clock.UtcNow;

        // The ceiling is resolved from the extension before a byte is read, so an oversized upload
        // stops at the limit instead of being written to disk in full and refused afterwards.
        var byName = fileTypes.CheckName(command.FileName, declaredMediaType: null, document.Type.Code);

        if (!byName.Accepted)
        {
            return Outcome.Fail<AddDocumentVersionResponse>("UNSUPPORTED_MEDIA_TYPE", byName.Message);
        }

        StagedContent staged;

        try
        {
            staged = await StagedContent.CreateAsync(command.Content, byName.MaxSizeBytes, ct);
        }
        catch (ContentTooLargeException tooLarge)
        {
            return Outcome.Fail<AddDocumentVersionResponse>("UNSUPPORTED_MEDIA_TYPE", tooLarge.Message);
        }

        await using var _ = staged;

        var decision = fileTypes.Inspect(
            command.FileName, staged.SizeBytes, staged.Header, staged.OpenRead, document.Type.Code);

        if (!decision.Accepted)
        {
            return Outcome.Fail<AddDocumentVersionResponse>(
                "UNSUPPORTED_MEDIA_TYPE", decision.Message!);
        }

        var blobId = BlobId.FromSha256(staged.Digest);
        var existing = await blobs.FindAsync(blobId, ct);

        if (existing is null || existing.IsPurged)
        {
            var storage = storages.Primary;
            var key = storage.KeyFor(staged.Digest);

            await using (var bytes = staged.OpenRead())
                await storage.PutAsync(
                    key, bytes, new ObjectMetadata(staged.SizeBytes, decision.MediaType), ct);

            if (existing is null)
            {
                await blobs.AddAsync(
                    Blob.Register(blobId, staged.SizeBytes, storage.Provider, key, now, actor), ct);
            }
            else
            {
                existing.Reactivate(now, actor);
            }
        }

        // The detected type of THIS content, not the previous version's. A scan replaced by a
        // searchable PDF is still the same document, and carrying the old media type forward would
        // make every download of it announce the wrong format.
        var version = document.AddVersion(
            blobId, name, new MimeType(decision.MediaType),
            staged.SizeBytes, command.Comment, now, actor);

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<AddDocumentVersionResponse>(
            new AddDocumentVersionResponse(version.Id.Value, version.Number.Value, blobId.Value));
    }
}
