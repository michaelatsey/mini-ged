using Ged.Domain.Folders;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Tests.Fakes;

namespace Ged.Features.Tests.Documents;

/// <summary>
/// Covers what an upload does to the blob it resolves to. Both cases here are ones a purge left
/// behind: a digest whose bytes are gone, and a digest the collector is entitled to remove.
/// </summary>
public sealed class UploadDocumentHandlerTests
{
    private static readonly Folder Target =
        Folder.CreateRoot(new FolderName("Dossiers"), FolderType.Category, Fixed.Now, Fixed.Me);

    [Fact]
    public async Task Content_whose_blob_was_purged_can_be_uploaded_again()
    {
        var fixture = new Fixture(Seed.Purged());

        var outcome = await fixture.UploadAsync();

        outcome.Succeeded.ShouldBeTrue();
        outcome.Value.ShouldNotBeNull().Deduplicated.ShouldBeFalse();

        var blob = (await fixture.Blobs.FindAsync(Content.Id)).ShouldNotBeNull();
        blob.Status.ShouldBe(BlobStatus.Active);
        blob.OrphanSince.ShouldBeNull();
        blob.Primary.ShouldNotBeNull().ObjectKey.ShouldBe(fixture.Storage.KeyFor(Content.Digest));

        fixture.Storage.Written.ShouldHaveSingleItem();
        fixture.Documents.Added.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deduplicating_against_an_orphan_candidate_closes_the_retention_window()
    {
        var fixture = new Fixture(Seed.OrphanCandidate());

        var outcome = await fixture.UploadAsync();

        outcome.Succeeded.ShouldBeTrue();
        outcome.Value.ShouldNotBeNull().Deduplicated.ShouldBeTrue();

        var blob = (await fixture.Blobs.FindAsync(Content.Id)).ShouldNotBeNull();
        blob.Status.ShouldBe(BlobStatus.Active);
        blob.OrphanSince.ShouldBeNull();

        // Nothing was written: the bytes are already there. The row, on the other hand, had to
        // change — an orphan candidate left untouched is one the collector may still purge, and
        // one no concurrency token can protect.
        fixture.Storage.Written.ShouldBeEmpty();
    }

    private sealed class Fixture
    {
        public Fixture(Blob? existing)
        {
            var journal = new Journal();

            Storage = new RecordingStorage(journal);
            UnitOfWork = new RecordingUnitOfWork(journal);

            if (existing is not null)
                Blobs.Seed(existing);

            Handler = new UploadDocumentHandler(
                new FakeFolderRepository(Target),
                Blobs,
                Documents,
                new SingleStorageRegistry(Storage),
                new PdfInspector(),
                UnitOfWork,
                new FixedClock(Fixed.Later));
        }

        public FakeBlobRepository Blobs { get; } = new();

        public FakeDocumentRepository Documents { get; } = new();

        public RecordingStorage Storage { get; }

        public RecordingUnitOfWork UnitOfWork { get; }

        public UploadDocumentHandler Handler { get; }

        public async Task<Outcome<UploadDocumentResponse>> UploadAsync()
        {
            await using var content = Content.Open();

            var outcome = await Handler.HandleAsync(new UploadDocumentCommand(
                Target.Id.Value, "invoice.pdf", null, content, Fixed.By));

            return outcome;
        }
    }
}
