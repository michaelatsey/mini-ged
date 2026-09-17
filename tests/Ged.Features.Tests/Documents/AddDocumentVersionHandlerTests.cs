using Ged.Domain.Documents;
using Ged.Domain.Folders;
using Ged.Features.Documents.AddDocumentVersion;
using Ged.Features.Tests.Fakes;

namespace Ged.Features.Tests.Documents;

/// <summary>
/// The version path resolves a digest exactly as the upload path does, so it inherits the same two
/// defects and needs the same two guarantees.
/// </summary>
public sealed class AddDocumentVersionHandlerTests
{
    private static readonly BlobId Previous = BlobId.FromSha256(new string('b', 64));

    [Fact]
    public async Task Content_whose_blob_was_purged_can_be_added_as_a_version()
    {
        var fixture = new Fixture(Seed.Purged());

        var outcome = await fixture.AddVersionAsync();

        outcome.Succeeded.ShouldBeTrue();
        outcome.Value.ShouldNotBeNull().VersionNumber.ShouldBe(2);

        var blob = (await fixture.Blobs.FindAsync(Content.Id)).ShouldNotBeNull();
        blob.Status.ShouldBe(BlobStatus.Active);
        blob.Primary.ShouldNotBeNull().ObjectKey.ShouldBe(fixture.Storage.KeyFor(Content.Digest));

        fixture.Storage.Written.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deduplicating_against_an_orphan_candidate_closes_the_retention_window()
    {
        var fixture = new Fixture(Seed.OrphanCandidate());

        var outcome = await fixture.AddVersionAsync();

        outcome.Succeeded.ShouldBeTrue();

        var blob = (await fixture.Blobs.FindAsync(Content.Id)).ShouldNotBeNull();
        blob.Status.ShouldBe(BlobStatus.Active);
        blob.OrphanSince.ShouldBeNull();

        fixture.Storage.Written.ShouldBeEmpty();
    }

    private sealed class Fixture
    {
        private readonly Document _document;

        public Fixture(Blob existing)
        {
            var journal = new Journal();

            Storage = new RecordingStorage(journal);
            UnitOfWork = new RecordingUnitOfWork(journal);
            Blobs.Seed(existing);

            _document = Document.Create(
                FolderId.New(),
                new DocumentName("invoice.pdf"),
                DocType.Unknown,
                Previous,
                new MimeType("application/pdf"),
                12,
                Fixed.Now,
                Fixed.Me);

            Documents.Seed(_document);

            Handler = new AddDocumentVersionHandler(
                Documents,
                Blobs,
                new SingleStorageRegistry(Storage),
                new PdfInspector(),
                UnitOfWork,
                new FixedClock(Fixed.Later));
        }

        public FakeBlobRepository Blobs { get; } = new();

        public FakeDocumentRepository Documents { get; } = new();

        public RecordingStorage Storage { get; }

        public RecordingUnitOfWork UnitOfWork { get; }

        public AddDocumentVersionHandler Handler { get; }

        public async Task<Outcome<AddDocumentVersionResponse>> AddVersionAsync()
        {
            await using var content = Content.Open();

            return await Handler.HandleAsync(new AddDocumentVersionCommand(
                _document.Id.Value, "invoice.pdf", "the signed copy", content, Fixed.By));
        }
    }
}
