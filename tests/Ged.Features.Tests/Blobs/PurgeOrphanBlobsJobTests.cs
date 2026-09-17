using Ged.Features.Blobs.Jobs;
using Ged.Features.Tests.Fakes;

namespace Ged.Features.Tests.Blobs;

/// <summary>
/// The collector is the only code in the system that deletes a byte, and a byte cannot be brought
/// back by a rollback. What these tests pin down is therefore not the end state but the order: what
/// must be committed before the irreversible step, and what must stop it.
/// </summary>
public sealed class PurgeOrphanBlobsJobTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly string Address = $"ged/{Content.Digest}";

    [Fact]
    public async Task The_row_is_marked_before_a_byte_is_removed()
    {
        var fixture = new Fixture();

        var report = await fixture.Job.RunAsync(Retention);

        report.Purged.ShouldBe(1);
        fixture.Storage.Deleted.ShouldHaveSingleItem();

        // The commit carries the concurrency token, so it is the step that detects a concurrent
        // upload. Deleting first means a detected conflict arrives after the bytes are gone.
        fixture.Journal.Entries.ShouldBe(["commit", $"delete:{Address}"]);
    }

    [Fact]
    public async Task A_blob_another_transaction_changed_keeps_its_bytes()
    {
        // The row an upload deduplicated against: reactivated, so the mark cannot commit.
        var fixture = new Fixture { FailAtCommit = 1, StatusInDatabase = BlobStatus.Active };

        var report = await fixture.Job.RunAsync(Retention);

        report.Purged.ShouldBe(0);
        report.Contended.ShouldBe(1);
        fixture.Storage.Deleted.ShouldBeEmpty();
        fixture.Journal.Entries.ShouldBe(["commit-failed"]);

        // Without the discard the aborted transition stays pending, and the next candidate's
        // commit either carries it or fails on the same token.
        fixture.UnitOfWork.Discards.ShouldBe(1);
    }

    [Fact]
    public async Task A_commit_that_failed_for_another_reason_is_not_reported_as_contention()
    {
        // Nothing touched the row, so the commit itself failed. Reporting that as a healthy batch
        // would leave the one job that deletes bytes claiming success while it wrote nothing.
        var fixture = new Fixture
        {
            FailAtCommit = 1,
            StatusInDatabase = BlobStatus.OrphanCandidate,
        };

        var act = async () => await fixture.Job.RunAsync(Retention);

        await act.ShouldThrowAsync<PersistenceException>();
        fixture.Storage.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_digest_uploaded_again_before_the_bytes_are_removed_keeps_them()
    {
        var fixture = new Fixture { StatusInDatabase = BlobStatus.Active };

        var report = await fixture.Job.RunAsync(Retention);

        report.Purged.ShouldBe(0);
        report.Contended.ShouldBe(1);
        fixture.Storage.Deleted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reference_that_reappeared_spares_the_content()
    {
        var fixture = new Fixture { LiveReferences = 1 };

        var report = await fixture.Job.RunAsync(Retention);

        report.Reactivated.ShouldBe(1);
        report.Purged.ShouldBe(0);
        fixture.Storage.Deleted.ShouldBeEmpty();

        var blob = (await fixture.Blobs.FindAsync(Content.Id)).ShouldNotBeNull();
        blob.Status.ShouldBe(BlobStatus.Active);
    }

    private sealed class Fixture
    {
        private PurgeOrphanBlobsJob? _job;
        private RecordingUnitOfWork? _unitOfWork;

        public Fixture()
        {
            Journal = new Journal();
            Storage = new RecordingStorage(Journal);
            Blobs = new FakeBlobRepository();
            Blobs.Seed(Seed.OrphanCandidate());
        }

        public int FailAtCommit { get; init; }

        public long LiveReferences { get; init; }

        /// <summary>What a read of the blob row answers, at whichever point the job asks.</summary>
        public BlobStatus StatusInDatabase { get; init; } = BlobStatus.Purged;

        public Journal Journal { get; }

        public RecordingStorage Storage { get; }

        public FakeBlobRepository Blobs { get; }

        public RecordingUnitOfWork UnitOfWork =>
            _unitOfWork ??= new RecordingUnitOfWork(Journal) { FailAtCommit = FailAtCommit };

        public PurgeOrphanBlobsJob Job => _job ??= new(
            new ScriptedDatabase
            {
                Candidates = [Content.Digest],
                ReferenceCount = _ => LiveReferences,
                Status = _ => StatusInDatabase.Code,
            },
            Blobs,
            new SingleStorageRegistry(Storage),
            UnitOfWork,
            new FixedClock(Fixed.Later));
    }
}
