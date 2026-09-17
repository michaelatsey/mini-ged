using Ged.Domain.Blobs;

namespace Ged.Domain.Tests.Blobs;

public sealed class BlobValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mi nio")]
    [InlineData("minio!")]
    public void StorageProvider_rejects_invalid_names(string raw) =>
        Should.Throw<DomainException>(() => new StorageProvider(raw));

    [Fact]
    public void StorageProvider_normalizes_case() =>
        new StorageProvider("MinIO").ShouldBe(StorageProvider.Minio);

    [Fact]
    public void ObjectKey_requires_both_parts()
    {
        Should.Throw<DomainException>(() => new ObjectKey("", "k"));
        Should.Throw<DomainException>(() => new ObjectKey("b", "  "));
        new ObjectKey("ged", "a/b").ToString().ShouldBe("ged/a/b");
    }

    [Fact]
    public void Lifecycle_codes_round_trip_and_reject_the_unknown()
    {
        BlobStatus.From("active").ShouldBe(BlobStatus.Active);
        LocationState.From("PRIMARY").ShouldBe(LocationState.Primary);
        Should.Throw<DomainException>(() => BlobStatus.From("DELETED"));
        Should.Throw<DomainException>(() => LocationState.From("BACKUP"));
    }

    [Fact]
    public void Only_a_copy_in_flight_is_unreadable()
    {
        LocationState.Migrating.IsReadable.ShouldBeFalse();
        LocationState.Replica.IsReadable.ShouldBeTrue();
        LocationState.Primary.IsReadable.ShouldBeTrue();
        LocationState.Legacy.IsReadable.ShouldBeTrue();
    }
}
