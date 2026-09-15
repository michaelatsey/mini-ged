using Ged.Domain.Blobs;
using Ged.Domain.Blobs.Identifiers;
using Ged.Domain.Documents;
using Ged.Domain.Documents.Identifiers;
using Ged.Domain.Documents.ValueObjects;

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
/// <param name="unitOfWork">Commits the change.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
public sealed class AddDocumentVersionHandler(
    IDocumentRepository documents,
    IBlobRepository blobs,
    IObjectStorageRegistry storages,
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

        await using var staged = await StagedContent.CreateAsync(command.Content, ct);

        var blobId = BlobId.FromSha256(staged.Digest);
        var existing = await blobs.FindAsync(blobId, ct);

        if (existing is null || existing.IsPurged)
        {
            var storage = storages.Primary;
            var key = storage.KeyFor(staged.Digest);

            await using (var bytes = staged.OpenRead())
                await storage.PutAsync(key, bytes, new ObjectMetadata(staged.SizeBytes, null), ct);

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

        var version = document.AddVersion(
            blobId, name, document.CurrentVersion.MimeType,
            staged.SizeBytes, command.Comment, now, actor);

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<AddDocumentVersionResponse>(
            new AddDocumentVersionResponse(version.Id.Value, version.Number.Value, blobId.Value));
    }
}
