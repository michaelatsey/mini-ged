using Ged.Core.Ports.FileTypes;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using Ged.Features.Common.FileTypes;
using Ged.Features.Documents.AddDocumentVersion;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Documents;

/// <summary>
/// Both write paths record a name, and both built it from the client's string before anything had
/// read the content. So a reclassified upload was stored under the extension the inspector had just
/// contradicted.
/// </summary>
/// <remarks>
/// The two slices share one inspector, which is what makes a new version of a photo follow the same
/// rule as its first upload — and what makes the two tests here worth having separately: the fix is
/// one line in each handler, and nothing else would notice if only one of them moved.
/// </remarks>
public sealed class ReclassifiedUploadNameTests
{
    private static readonly DetectedFormat Png = new("png", "image/png");

    private static readonly Folder Target =
        Folder.CreateRoot(new FolderName("Photos"), FolderType.Category, Fixed.Now, Fixed.Me);

    private static readonly BlobId Previous = BlobId.FromSha256(new string('b', 64));

    [Fact]
    public async Task An_uploaded_png_named_jpg_is_stored_under_its_real_extension()
    {
        var blobs = new FakeBlobRepository();
        var documents = new FakeDocumentRepository();
        var journal = new Journal();

        var handler = new UploadDocumentHandler(
            new FakeFolderRepository(Target),
            blobs,
            documents,
            new SingleStorageRegistry(new RecordingStorage(journal)),
            Inspector(),
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Now));

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new UploadDocumentCommand(
            Target.Id.Value, "photo.jpg", null, content, Fixed.By));

        outcome.Succeeded.ShouldBeTrue();

        var document = documents.Added.ShouldHaveSingleItem();
        document.Name.Value.ShouldBe("photo.png");
        document.Versions.ShouldHaveSingleItem().FileName.Value.ShouldBe("photo.png");
        document.Versions[0].MimeType.Value.ShouldBe("image/png");
    }

    [Fact]
    public async Task A_new_version_follows_the_same_rule_as_the_first_upload()
    {
        var documents = new FakeDocumentRepository();
        var journal = new Journal();

        var document = Document.Create(
            FolderId.New(),
            new DocumentName("photo.png"),
            DocType.Unknown,
            Previous,
            new MimeType("image/png"),
            12,
            Fixed.Now,
            Fixed.Me);

        documents.Seed(document);

        var handler = new AddDocumentVersionHandler(
            documents,
            new FakeBlobRepository(),
            new SingleStorageRegistry(new RecordingStorage(journal)),
            Inspector(),
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Later));

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new AddDocumentVersionCommand(
            document.Id.Value, "photo.jpg", "the one the phone sent", content, Fixed.By));

        outcome.Succeeded.ShouldBeTrue();

        var version = document.Versions[^1];
        version.FileName.Value.ShouldBe("photo.png");
        version.MimeType.Value.ShouldBe("image/png");
    }

    /// <summary>The real inspector over a policy that forgives one image for another.</summary>
    /// <remarks>
    /// The real one, not a fake: the name a handler stores comes from the decision, so a fake
    /// returning a hand-built decision would be asserting on the test's own arithmetic.
    /// </remarks>
    private static FileTypeInspector Inspector() => new(
        new StubDetector(Png),
        Options.Create(new UploadPolicyOptions
        {
            AllowedFormats = ["jpeg", "png"],
            ReclassifiableGroups = ["images"],
        }));
}
