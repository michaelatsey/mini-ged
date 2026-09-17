namespace Ged.Domain.Blobs;

/// <summary>
/// One place where a blob's bytes physically live, and the role that place currently plays.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <see cref="Blob"/> aggregate rather than an aggregate of its own. Promoting one
/// location and demoting another must happen in a single transaction — a blob with two primaries,
/// or none, has no defined read path — and that requirement is exactly what an aggregate boundary
/// is for.
/// </para>
/// <para>
/// The address is fixed at creation; only the state moves. The constructor is
/// <see langword="internal"/> so <see cref="Blob"/> is the only possible author.
/// </para>
/// </remarks>
public sealed class BlobLocation : Entity<BlobLocationId>
{
    internal BlobLocation(
        BlobLocationId id,
        BlobId blobId,
        StorageProvider provider,
        ObjectKey objectKey,
        LocationState state,
        DateTimeOffset registeredAt)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(objectKey);
        ArgumentNullException.ThrowIfNull(state);

        BlobId = blobId;
        Provider = provider;
        ObjectKey = objectKey;
        State = state;
        RegisteredAt = registeredAt;
    }

    /// <summary>Gets the blob whose content lives here.</summary>
    public BlobId BlobId { get; private init; }

    /// <summary>Gets the storage backend holding the content.</summary>
    public StorageProvider Provider { get; private init; }

    /// <summary>Gets the address within that backend.</summary>
    public ObjectKey ObjectKey { get; private set; }

    /// <summary>Gets the role this location currently plays.</summary>
    public LocationState State { get; private set; }

    /// <summary>Gets when this location was registered, in UTC.</summary>
    public DateTimeOffset RegisteredAt { get; private init; }

    /// <summary>
    /// Gets when the copy was last confirmed to match the blob's digest, or null when it never was.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Gets a value indicating whether content can be read from this location.</summary>
    public bool IsReadable => State.IsReadable;

    /// <summary>Materialisation constructor, used by the persistence layer only.</summary>
    /// <param name="id">The location identifier.</param>
    /// <param name="blobId">The blob whose content lives here.</param>
    /// <param name="provider">The storage backend.</param>
    /// <param name="state">The role this location plays.</param>
    /// <param name="registeredAt">When the location was registered.</param>
    /// <remarks>
    /// <para>
    /// <see cref="ObjectKey"/> is absent from this signature on purpose. It maps to more than one
    /// column, so a persistence provider treats it as a nested structure rather than as a value —
    /// and a structure cannot be handed to a constructor, only assigned afterwards. Leaving it out
    /// is what allows the rest of the state to be restored in one step.
    /// </para>
    /// <para>
    /// <see langword="private"/>, and deliberately not a substitute for the constructor above: the
    /// domain still has exactly one way to create a location, and it requires an address.
    /// </para>
    /// </remarks>
    private BlobLocation(
        BlobLocationId id,
        BlobId blobId,
        StorageProvider provider,
        LocationState state,
        DateTimeOffset registeredAt)
        : base(id)
    {
        BlobId = blobId;
        Provider = provider;
        State = state;
        RegisteredAt = registeredAt;

        // Assigned immediately afterwards by the provider. There is no instant at which an
        // application can observe this object, so the null never escapes.
        ObjectKey = null!;
    }

    internal void Verify(DateTimeOffset verifiedAt)
    {
        State = LocationState.Replica;
        VerifiedAt = verifiedAt;
    }

    internal void ChangeState(LocationState state) => State = state;
}
