namespace Ged.Features.Tests;

/// <summary>Blobs in the lifecycle states the upload path has to cope with.</summary>
internal static class Seed
{
    private static readonly ObjectKey Key = new("ged", $"ged/{Content.Digest}");

    /// <summary>Content nothing references any more, still inside its retention window.</summary>
    public static Blob OrphanCandidate()
    {
        var blob = Registered();
        blob.MarkOrphanCandidate(hasLiveReferences: false, Fixed.Now, Fixed.Me);

        return blob;
    }

    /// <summary>Content the collector has already removed from every backend.</summary>
    public static Blob Purged()
    {
        var blob = OrphanCandidate();
        blob.MarkPurged(hasLiveReferences: false, Fixed.Later, Fixed.Later, Fixed.Me);

        return blob;
    }

    /// <summary>Content at least one live version still references.</summary>
    public static Blob Registered() => Blob.Register(
        Content.Id, Content.Bytes.Length, StorageProvider.Beys, Key, Fixed.Now, Fixed.Me);
}
