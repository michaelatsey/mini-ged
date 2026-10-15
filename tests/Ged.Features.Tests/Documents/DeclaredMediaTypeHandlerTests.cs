using Ged.Adapters.FileTypes;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using Ged.Features.Common.FileTypes;
using Ged.Features.Documents.AddDocumentVersion;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Documents;

/// <summary>
/// The declared media type travels from the command to both checks, on both upload slices.
/// </summary>
/// <remarks>
/// The handlers used to pass a null declared type to the name check and none at all to the content
/// check, so the comparison ran only at the endpoint, against the extension. Each slice is tested
/// twice: once with a spy, proving the command's value reaches both calls; once end to end with the
/// real inspector and detector on PDF bytes, proving a contradicted bare upload stores nothing.
/// </remarks>
public sealed class DeclaredMediaTypeHandlerTests
{
    [Fact]
    public async Task An_upload_passes_its_declared_type_to_both_checks()
    {
        var spy = new SpyInspector();
        var (handler, _, _) = Upload(spy);

        await using var content = Content.Open();

        await handler.HandleAsync(new UploadDocumentCommand(
            Target.Id.Value, "rapport", "application/pdf", null, content, Fixed.By));

        spy.Declared.ShouldBe(["application/pdf", "application/pdf"]);
    }

    [Fact]
    public async Task A_version_passes_its_declared_type_to_both_checks()
    {
        var spy = new SpyInspector();
        var (handler, _) = AddVersion(spy);

        await using var content = Content.Open();

        await handler.HandleAsync(new AddDocumentVersionCommand(
            Existing.Id.Value, "scan", "application/pdf", "from the copier", content, Fixed.By));

        spy.Declared.ShouldBe(["application/pdf", "application/pdf"]);
    }

    [Fact]
    public async Task A_bare_upload_declared_as_something_its_content_is_not_stores_nothing()
    {
        // The outcome carries no RejectionReason, and the message is not asserted on; the positive
        // twin below — same bytes, same name, a true claim — is what pins the refusal to the claim.
        var (handler, documents, storage) = Upload(RealInspector());

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new UploadDocumentCommand(
            Target.Id.Value, "rapport", "image/png", null, content, Fixed.By));

        outcome.Succeeded.ShouldBeFalse();
        documents.Added.ShouldBeEmpty();
        storage.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bare_upload_declared_as_its_content_is_stored_under_its_extension()
    {
        var (handler, documents, _) = Upload(RealInspector());

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new UploadDocumentCommand(
            Target.Id.Value, "rapport", "application/pdf", null, content, Fixed.By));

        outcome.Succeeded.ShouldBeTrue();

        var document = documents.Added.ShouldHaveSingleItem();
        document.Name.Value.ShouldBe("rapport.pdf");
        document.Versions.ShouldHaveSingleItem().MimeType.Value.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task A_bare_version_declared_as_its_content_is_stored_under_its_extension()
    {
        // The positive twin of the refusal below: the same bytes and the same name, so that refusal can
        // only come from the declared type.
        var (handler, _) = AddVersion(RealInspector());

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new AddDocumentVersionCommand(
            Existing.Id.Value, "scan", "application/pdf", "from the copier", content, Fixed.By));

        outcome.Succeeded.ShouldBeTrue();

        var version = Existing.Versions[^1];
        version.FileName.Value.ShouldBe("scan.pdf");
        version.MimeType.Value.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task A_bare_version_declared_as_something_its_content_is_not_stores_nothing()
    {
        var (handler, storage) = AddVersion(RealInspector());
        var versions = Existing.Versions.Count;

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new AddDocumentVersionCommand(
            Existing.Id.Value, "scan", "image/png", "from the copier", content, Fixed.By));

        outcome.Succeeded.ShouldBeFalse();
        Existing.Versions.Count.ShouldBe(versions);
        storage.Written.ShouldBeEmpty();
    }

    private Folder Target { get; } =
        Folder.CreateRoot(new FolderName("Scans"), FolderType.Category, Fixed.Now, Fixed.Me);

    private Document Existing { get; } = Document.Create(
        FolderId.New(),
        new DocumentName("contrat.pdf"),
        DocType.Unknown,
        BlobId.FromSha256(new string('c', 64)),
        new MimeType("application/pdf"),
        12,
        Fixed.Now,
        Fixed.Me);

    private (UploadDocumentHandler Handler, FakeDocumentRepository Documents, RecordingStorage Storage) Upload(
        IFileTypeInspector inspector)
    {
        var journal = new Journal();
        var documents = new FakeDocumentRepository();
        var storage = new RecordingStorage(journal);

        var handler = new UploadDocumentHandler(
            new FakeFolderRepository(Target),
            new FakeBlobRepository(),
            documents,
            new SingleStorageRegistry(storage),
            inspector,
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Now));

        return (handler, documents, storage);
    }

    private (AddDocumentVersionHandler Handler, RecordingStorage Storage) AddVersion(IFileTypeInspector inspector)
    {
        var journal = new Journal();
        var documents = new FakeDocumentRepository();
        var storage = new RecordingStorage(journal);

        documents.Seed(Existing);

        var handler = new AddDocumentVersionHandler(
            documents,
            new FakeBlobRepository(),
            new SingleStorageRegistry(storage),
            inspector,
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Later));

        return (handler, storage);
    }

    private static FileTypeInspector RealInspector() => new(
        new BuiltInContentFormatDetector(),
        Options.Create(new UploadPolicyOptions
        {
            AllowedFormats = ["pdf", "png"],
            EnforceDeclaredMediaType = true,
            AllowMissingExtension = true,
        }),
        NullLogger<FileTypeInspector>.Instance);

    /// <summary>Accepts everything as a PDF, and records the declared type each check received.</summary>
    private sealed class SpyInspector : IFileTypeInspector
    {
        private static readonly FileFormat Pdf = new("pdf", "application/pdf", ["pdf"]);

        public List<string?> Declared { get; } = [];

        public FileTypeDecision CheckName(string fileName, string? declaredMediaType, string? docType)
        {
            Declared.Add(declaredMediaType);

            return FileTypeDecision.Accept(Pdf, 10 * 1024 * 1024, reclassified: true);
        }

        public FileTypeDecision Inspect(
            string fileName,
            string? declaredMediaType,
            long sizeBytes,
            ReadOnlySpan<byte> header,
            Func<Stream> openContent,
            string? docType)
        {
            Declared.Add(declaredMediaType);

            return FileTypeDecision.Accept(Pdf, 10 * 1024 * 1024, reclassified: true);
        }
    }
}
