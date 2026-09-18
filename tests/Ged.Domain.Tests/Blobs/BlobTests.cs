using Ged.Domain.Blobs;
using Ged.Domain.Blobs.Events;
using Ged.Domain.Blobs.Rules;

namespace Ged.Domain.Tests.Blobs;

public sealed class BlobTests
{
    private static readonly BlobId Digest = BlobId.FromSha256(new string('a', 64));
    private static readonly ObjectKey OnBeys = new("legacy", "beys/00112233");
    private static readonly ObjectKey OnMinio = new("ged", "ged/" + new string('a', 64));
    private static readonly StorageProvider FileSystem = new("filesystem");
    private static readonly ObjectKey OnFileSystem = new("data", "fs/" + new string('a', 64));

    private static Blob Registered() =>
        Blob.Register(Digest, 1024, StorageProvider.Beys, OnBeys, Fixed.Now, Fixed.Me);

    private static Blob Purged()
    {
        var blob = Registered();
        blob.MarkOrphanCandidate(false, Fixed.Later, Fixed.Me);
        blob.MarkPurged(false, Fixed.Later.AddDays(31), Fixed.Later.AddDays(31), Fixed.Me);

        return blob;
    }

    [Fact]
    public void Register_points_at_the_location_it_creates()
    {
        var blob = Registered();

        var only = blob.Locations.ShouldHaveSingleItem();
        blob.PrimaryLocationId.ShouldBe(only.Id);
        blob.Primary.ShouldNotBeNull().Provider.ShouldBe(StorageProvider.Beys);
        blob.Status.ShouldBe(BlobStatus.Active);
        blob.SizeBytes.ShouldBe(1024);
    }

    [Fact]
    public void Register_records_the_first_location_verified()
    {
        var blob = Registered();

        // The caller has just written these bytes and the blob's own id is their digest, so the
        // copy is verified by construction. Left unverified it is a readable location nobody has
        // checked, which is the one thing a content-addressed model exists to prevent.
        var only = blob.Locations.ShouldHaveSingleItem();
        only.State.ShouldBe(LocationState.Replica);
        only.VerifiedAt.ShouldBe(Fixed.Now);
    }

    [Fact]
    public void A_copy_in_flight_is_not_readable()
    {
        var blob = Registered();

        var location = blob.AddLocation(StorageProvider.Minio, OnMinio, true, Fixed.Later, Fixed.Me);

        location.State.ShouldBe(LocationState.Migrating);
        location.IsReadable.ShouldBeFalse();
        blob.ReadOrder.ShouldHaveSingleItem();
    }

    [Fact]
    public void An_unverified_copy_cannot_serve_reads()
    {
        var blob = Registered();
        var location = blob.AddLocation(StorageProvider.Minio, OnMinio, true, Fixed.Later, Fixed.Me);

        var act = () => blob.PromoteToPrimary(location.Id, Fixed.Later, Fixed.Me);

        act.ShouldBreak<LocationMustBeVerifiedBeforePromotionRule>();
    }

    [Fact]
    public void A_provider_migration_is_data_transitions_and_no_code_change()
    {
        var blob = Registered();

        var target = blob.AddLocation(StorageProvider.Minio, OnMinio, true, Fixed.Later, Fixed.Me);
        blob.VerifyLocation(target.Id, Fixed.Later, Fixed.Me);
        target.State.ShouldBe(LocationState.Replica);

        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);
        blob.PrimaryLocationId.ShouldBe(target.Id);
        blob.Primary.ShouldNotBeNull().Provider.ShouldBe(StorageProvider.Minio);

        var superseded = blob.Locations.Single(l => l.Provider == StorageProvider.Beys);
        superseded.State.ShouldBe(LocationState.Legacy);

        blob.RemoveLocation(superseded.Id, Fixed.Latest, Fixed.Me);
        blob.Locations.ShouldHaveSingleItem();

        var switched = blob.DomainEvents.OfType<BlobPrimarySwitched>().ShouldHaveSingleItem();
        switched.PreviousLocationId.ShouldBe(superseded.Id.Value);
        switched.PreviousProvider.ShouldBe("beys");
        switched.NewProvider.ShouldBe("minio");
    }

    [Fact]
    public void Promoting_moves_the_pointer_and_supersedes_the_old_location()
    {
        var blob = Registered();
        var previous = blob.Primary.ShouldNotBeNull();
        var target = blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);

        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);

        blob.PrimaryLocationId.ShouldBe(target.Id);
        target.State.ShouldBe(LocationState.Replica);
        previous.State.ShouldBe(LocationState.Legacy);
    }

    [Fact]
    public void A_rollback_is_the_same_call_in_the_other_direction()
    {
        var blob = Registered();
        var beys = blob.Primary.ShouldNotBeNull();
        var minio = blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);

        blob.PromoteToPrimary(minio.Id, Fixed.Later, Fixed.Me);
        blob.PromoteToPrimary(beys.Id, Fixed.Latest, Fixed.Me);

        // The sequence the old partial unique index rejected: rolling back promotes the older row,
        // whose update EF emits first. With the pointer there is no index and no ordering to get
        // right, so the two directions are the same call.
        blob.PrimaryLocationId.ShouldBe(beys.Id);
        beys.State.ShouldBe(LocationState.Replica);
        minio.State.ShouldBe(LocationState.Legacy);
        blob.ReadOrder[0].Provider.ShouldBe(StorageProvider.Beys);
    }

    [Fact]
    public void Promoting_where_no_location_served_reads_reports_no_previous_location()
    {
        var blob = Registered();
        var target = blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);

        // What 0008 backfilled for a blob whose serving location had been removed before reads
        // became a pointer: active, no pointer. No method produces it, so the pointer is cleared
        // the way EF materializes a NULL column — through its private setter.
        typeof(Blob).GetProperty(nameof(Blob.PrimaryLocationId))!.SetValue(blob, null);

        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);

        // Guid.Empty used to stand for "none" here, and BlobLocationId.From refuses it: a consumer
        // resolving the previous location to invalidate its cache failed on exactly this case.
        var switched = blob.DomainEvents.OfType<BlobPrimarySwitched>().ShouldHaveSingleItem();
        switched.PreviousLocationId.ShouldBeNull();
        switched.PreviousProvider.ShouldBeNull();
        switched.NewLocationId.ShouldBe(target.Id.Value);
    }

    [Fact]
    public void The_location_serving_reads_cannot_be_removed()
    {
        var blob = Registered();
        blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);

        // A readable copy survives, so the last-location rule is satisfied and the removal used to
        // go through — leaving an active blob whose reads pointed nowhere.
        var act = () => blob.RemoveLocation(blob.Primary!.Id, Fixed.Latest, Fixed.Me);

        act.ShouldBreak<LocationMustNotBeServingReadsRule>();
    }

    [Fact]
    public void The_last_readable_location_cannot_be_removed()
    {
        var blob = Registered();

        var act = () => blob.RemoveLocation(blob.Primary!.Id, Fixed.Later, Fixed.Me);

        act.ShouldBreak<BlobMustKeepAReadableLocationRule>();
    }

    [Fact]
    public void The_same_address_cannot_be_registered_twice()
    {
        var blob = Registered();

        Action act = () => blob.AddLocation(StorageProvider.Beys, OnBeys, false, Fixed.Later, Fixed.Me);

        act.ShouldBreak<LocationMustNotAlreadyExistRule>();
    }

    [Fact]
    public void A_referenced_blob_cannot_become_an_orphan_candidate()
    {
        var blob = Registered();

        var act = () => blob.MarkOrphanCandidate(true, Fixed.Later, Fixed.Me);

        act.ShouldBreak<BlobMustHaveNoLiveReferencesRule>();
    }

    [Fact]
    public void Reactivation_closes_the_retention_window()
    {
        var blob = Registered();
        blob.MarkOrphanCandidate(false, Fixed.Later, Fixed.Me);
        blob.OrphanSince.ShouldBe(Fixed.Later);

        blob.Reactivate(Fixed.Latest, Fixed.Me);

        blob.Status.ShouldBe(BlobStatus.Active);
        blob.OrphanSince.ShouldBeNull();
        blob.DomainEvents.OfType<BlobReactivated>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Purging_requires_an_orphan_that_outlived_the_retention_window()
    {
        var blob = Registered();

        var whileActive = () => blob.MarkPurged(false, Fixed.Later, Fixed.Later, Fixed.Me);
        whileActive.ShouldBreak<BlobMustBeAnAgedOrphanToPurgeRule>();

        blob.MarkOrphanCandidate(false, Fixed.Later, Fixed.Me);

        var tooSoon = () => blob.MarkPurged(false, Fixed.Later.AddDays(-1), Fixed.Later, Fixed.Me);
        tooSoon.ShouldBreak<BlobMustBeAnAgedOrphanToPurgeRule>();
    }

    [Fact]
    public void Purging_re_checks_references_immediately_before_removing()
    {
        var blob = Registered();
        blob.MarkOrphanCandidate(false, Fixed.Later, Fixed.Me);

        var act = () => blob.MarkPurged(true, Fixed.Later.AddDays(31), Fixed.Later.AddDays(31), Fixed.Me);

        act.ShouldBreak<BlobMustHaveNoLiveReferencesRule>();
    }

    [Fact]
    public void Purging_clears_every_location_and_is_terminal()
    {
        var blob = Registered();
        blob.MarkOrphanCandidate(false, Fixed.Later, Fixed.Me);
        var cutoff = Fixed.Later.AddDays(31);

        blob.MarkPurged(false, cutoff, cutoff, Fixed.Me);

        blob.IsPurged.ShouldBeTrue();
        blob.Locations.ShouldBeEmpty();
        blob.PrimaryLocationId.ShouldBeNull();
        blob.DomainEvents.OfType<BlobPurged>().ShouldHaveSingleItem().SizeBytes.ShouldBe(1024);

        var act = () => blob.Reactivate(cutoff.AddDays(1), Fixed.Me);
        act.ShouldBreak<BlobMustNotBePurgedRule>();
    }

    [Fact]
    public void A_purged_digest_uploaded_again_comes_back_with_a_primary()
    {
        var blob = Purged();

        blob.Restore(StorageProvider.Minio, OnMinio, Fixed.Latest, Fixed.Me);

        blob.Status.ShouldBe(BlobStatus.Active);
        blob.OrphanSince.ShouldBeNull();
        blob.IsPurged.ShouldBeFalse();

        // The point of the transition: a revived row with no location has no read path, whatever
        // its status claims.
        blob.PrimaryLocationId.ShouldBe(blob.Locations.ShouldHaveSingleItem().Id);
        blob.Primary.ShouldNotBeNull().ObjectKey.ShouldBe(OnMinio);
        blob.ReadOrder.ShouldHaveSingleItem();

        var restored = blob.DomainEvents.OfType<BlobRestored>().ShouldHaveSingleItem();
        restored.SizeBytes.ShouldBe(1024);
        restored.Provider.ShouldBe("minio");
    }

    [Fact]
    public void Only_a_purged_blob_can_be_restored()
    {
        var active = Registered();

        var whileActive = () => active.Restore(
            StorageProvider.Minio, OnMinio, Fixed.Later, Fixed.Me);
        whileActive.ShouldBreak<BlobMustBePurgedToRestoreRule>();

        var candidate = Registered();
        candidate.MarkOrphanCandidate(false, Fixed.Later, Fixed.Me);

        var whileCandidate = () => candidate.Restore(
            StorageProvider.Minio, OnMinio, Fixed.Latest, Fixed.Me);
        whileCandidate.ShouldBreak<BlobMustBePurgedToRestoreRule>();
    }

    [Fact]
    public void ReadOrder_is_primary_then_replica_then_legacy()
    {
        var blob = Registered();
        blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);
        var target = blob.AddLocation(FileSystem, OnFileSystem, false, Fixed.Later, Fixed.Me);

        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);

        // Three locations, and the one serving reads was added last. Both it and minio are REPLICA,
        // so ranking by state alone would put minio first — only the pointer separates them. With
        // two locations this assertion holds either way, which is why there are three.
        blob.ReadOrder[0].Provider.ShouldBe(FileSystem);
        blob.ReadOrder[1].Provider.ShouldBe(StorageProvider.Minio);
        blob.ReadOrder[2].Provider.ShouldBe(StorageProvider.Beys);
    }

    [Fact]
    public void An_unknown_location_is_rejected()
    {
        var blob = Registered();

        var act = () => blob.VerifyLocation(BlobLocationId.New(), Fixed.Later, Fixed.Me);

        act.ShouldBreak<LocationMustBelongToBlobRule>();
    }
}
