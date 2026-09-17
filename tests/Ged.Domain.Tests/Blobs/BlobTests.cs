using Ged.Domain.Blobs;
using Ged.Domain.Blobs.Events;
using Ged.Domain.Blobs.Rules;

namespace Ged.Domain.Tests.Blobs;

public sealed class BlobTests
{
    private static readonly BlobId Digest = BlobId.FromSha256(new string('a', 64));
    private static readonly ObjectKey OnBeys = new("legacy", "beys/00112233");
    private static readonly ObjectKey OnMinio = new("ged", "ged/" + new string('a', 64));

    private static Blob Registered() =>
        Blob.Register(Digest, 1024, StorageProvider.Beys, OnBeys, Fixed.Now, Fixed.Me);

    [Fact]
    public void Register_creates_a_primary_location()
    {
        var blob = Registered();

        blob.Locations.ShouldHaveSingleItem();
        blob.Primary.ShouldNotBeNull().Provider.ShouldBe(StorageProvider.Beys);
        blob.Status.ShouldBe(BlobStatus.Active);
        blob.SizeBytes.ShouldBe(1024);
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
    public void A_provider_migration_is_four_state_changes_and_no_code_change()
    {
        var blob = Registered();

        var target = blob.AddLocation(StorageProvider.Minio, OnMinio, true, Fixed.Later, Fixed.Me);
        blob.VerifyLocation(target.Id, Fixed.Later, Fixed.Me);
        target.State.ShouldBe(LocationState.Replica);

        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);
        blob.Primary.ShouldNotBeNull().Provider.ShouldBe(StorageProvider.Minio);

        var superseded = blob.Locations.Single(l => l.Provider == StorageProvider.Beys);
        superseded.State.ShouldBe(LocationState.Legacy);

        blob.RemoveLocation(superseded.Id, Fixed.Latest, Fixed.Me);
        blob.Locations.ShouldHaveSingleItem();

        var switched = blob.DomainEvents.OfType<BlobPrimarySwitched>().ShouldHaveSingleItem();
        switched.PreviousProvider.ShouldBe("beys");
        switched.NewProvider.ShouldBe("minio");
    }

    [Fact]
    public void There_is_never_more_than_one_primary()
    {
        var blob = Registered();
        var target = blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);

        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);

        blob.Locations.Count(l => l.State == LocationState.Primary).ShouldBe(1);
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
        blob.DomainEvents.OfType<BlobPurged>().ShouldHaveSingleItem().SizeBytes.ShouldBe(1024);

        var act = () => blob.Reactivate(cutoff.AddDays(1), Fixed.Me);
        act.ShouldBreak<BlobMustNotBePurgedRule>();
    }

    [Fact]
    public void ReadOrder_is_primary_then_replica_then_legacy()
    {
        var blob = Registered();
        var target = blob.AddLocation(StorageProvider.Minio, OnMinio, false, Fixed.Later, Fixed.Me);
        blob.PromoteToPrimary(target.Id, Fixed.Later, Fixed.Me);

        blob.ReadOrder[0].Provider.ShouldBe(StorageProvider.Minio);
        blob.ReadOrder[^1].Provider.ShouldBe(StorageProvider.Beys);
    }

    [Fact]
    public void An_unknown_location_is_rejected()
    {
        var blob = Registered();

        var act = () => blob.VerifyLocation(BlobLocationId.New(), Fixed.Later, Fixed.Me);

        act.ShouldBreak<LocationMustBelongToBlobRule>();
    }
}
