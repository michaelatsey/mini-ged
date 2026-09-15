using Ged.Domain.Blobs;
using Ged.Domain.Blobs.Identifiers;
using Ged.Domain.Documents;
using Ged.Domain.Documents.ValueObjects;
using Ged.Domain.Folders;
using Ged.Domain.Folders.Identifiers;

namespace Ged.Features.Documents.UploadDocument;

/// <summary>Creates a document from an uploaded file.</summary>
/// <param name="FolderId">The folder the document belongs to.</param>
/// <param name="FileName">The file name as supplied by the client.</param>
/// <param name="DocType">The business classification, or null for unknown.</param>
/// <param name="Content">The bytes. Read once, forward-only.</param>
/// <param name="By">The acting identity.</param>
public sealed record UploadDocumentCommand(
    Guid FolderId,
    string FileName,
    string? DocType,
    Stream Content,
    string By);

/// <summary>What the caller gets back after an upload.</summary>
/// <param name="DocumentId">The new document.</param>
/// <param name="VersionId">Its first version.</param>
/// <param name="BlobId">The digest of the content.</param>
/// <param name="Deduplicated">
/// True when the content already existed and no bytes were written to storage.
/// </param>
public sealed record UploadDocumentResponse(
    Guid DocumentId, Guid VersionId, string BlobId, bool Deduplicated);

/// <summary>Handles <see cref="UploadDocumentCommand"/>.</summary>
/// <param name="folders">Verifies the target folder.</param>
/// <param name="blobs">Finds or registers the content.</param>
/// <param name="documents">Stages the new document.</param>
/// <param name="storages">Supplies the backend content is written to.</param>
/// <param name="unitOfWork">Commits both aggregates together.</param>
/// <param name="clock">Supplies the instant of the operation.</param>
public sealed class UploadDocumentHandler(
    IFolderRepository folders,
    IBlobRepository blobs,
    IDocumentRepository documents,
    IObjectStorageRegistry storages,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// The order of the last two steps is the point of this handler. Content is written to storage
    /// <em>before</em> the transaction opens, never inside it. Writing within the transaction would
    /// hold it open for the length of an upload, and a failed commit would leave an object nothing
    /// in the database refers to — a fantôme no job can find.
    /// </para>
    /// <para>
    /// Written first and committed after, a failure leaves a plain orphan instead: an object with no
    /// row, which the reconciliation job detects precisely because it walks storage looking for
    /// exactly that.
    /// </para>
    /// </remarks>
    public async Task<Outcome<UploadDocumentResponse>> HandleAsync(
        UploadDocumentCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var folderId = FolderId.From(command.FolderId);
        var folder = await folders.FindAsync(folderId, ct);

        if (folder is null || folder.IsDeleted)
            return Outcome.NotFound<UploadDocumentResponse>("Folder");

        var actor = new Actor(command.By);
        var name = new DocumentName(command.FileName);
        var docType = new DocType(command.DocType ?? DocType.Unknown.Code);
        var now = clock.UtcNow;

        await using var staged = await StagedContent.CreateAsync(command.Content, ct);

        var blobId = BlobId.FromSha256(staged.Digest);
        var existing = await blobs.FindAsync(blobId, ct);
        var deduplicated = existing is not null && !existing.IsPurged;

        if (!deduplicated)
        {
            var storage = storages.Primary;
            var key = storage.KeyFor(staged.Digest);

            // Outside the transaction, deliberately. See the remarks above.
            await using (var bytes = staged.OpenRead())
            {
                await storage.PutAsync(
                    key, bytes, new ObjectMetadata(staged.SizeBytes, MimeTypeOf(name)), ct);
            }

            if (existing is null)
            {
                var blob = Blob.Register(
                    blobId, staged.SizeBytes, storage.Provider, key, now, actor);

                await blobs.AddAsync(blob, ct);
            }
            else
            {
                // The digest was purged and has been uploaded again. The row is kept for audit, so
                // the blob is revived rather than re-registered.
                existing.Reactivate(now, actor);
            }
        }

        var document = Document.Create(
            folderId, name, docType, blobId,
            new MimeType(MimeTypeOf(name)), staged.SizeBytes, now, actor);

        await documents.AddAsync(document, ct);

        await unitOfWork.CommitAsync(ct);

        return Outcome.Ok<UploadDocumentResponse>(new UploadDocumentResponse(
            document.Id.Value, document.CurrentVersionId.Value, blobId.Value, deduplicated));
    }

    // The declared content type is not trusted, and sniffing bytes is an ingestion concern rather
    // than a use-case one. The extension is a hint; a scanner slotted in later can correct it.
    private static string MimeTypeOf(DocumentName name) => name.Extension switch
    {
        "pdf" => "application/pdf",
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "txt" => "text/plain",
        "csv" => "text/csv",
        "json" => "application/json",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };
}
