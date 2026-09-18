using Ged.Domain.Blobs.Events;
using Ged.Domain.Blobs.Rules;

namespace Ged.Domain.Blobs;

/// <summary>
/// A piece of stored content, identified by the digest of its own bytes, together with every
/// place those bytes currently live.
/// </summary>
/// <remarks>
/// <para>
/// Content is immutable: the identifier <em>is</em> the content, so size never changes and the
/// bytes are never rewritten. Two documents referencing the same digest are referencing the same
/// object, and neither can alter what the other sees — which is what makes sharing safe rather
/// than dangerous.
/// </para>
/// <para>
/// Locations belong to this aggregate because switching which one serves reads must be atomic: a
/// blob with no defined read path is unreadable content. Everything else about a location —
/// creating the object, copying it, deleting it — happens in infrastructure. This aggregate only
/// records what is true.
/// </para>
/// <para>
/// The lifecycle never destroys anything. <see cref="MarkOrphanCandidate"/> opens a retention
/// window, <see cref="Reactivate"/> closes it if a reference reappears, and
/// <see cref="MarkPurged"/> records a removal a collector already performed. No method on this
/// type deletes a byte.
/// </para>
/// <para>
/// <see cref="Restore"/> is the way back from a purge, and it exists because the identifier is the
/// content: uploading the same file again resolves to this very row, which is kept for audit and
/// cannot be registered a second time.
/// </para>
/// </remarks>
public sealed class Blob : AuditableAggregateRoot<BlobId>
{
    private readonly List<BlobLocation> _locations = [];

    private Blob(BlobId id, long sizeBytes, DateTimeOffset createdAt, Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        SizeBytes = sizeBytes;
        Status = BlobStatus.Active;
    }

    /// <summary>Gets the size of the content, in bytes. Fixed for the lifetime of the blob.</summary>
    public long SizeBytes { get; private init; }

    /// <summary>Gets where the blob sits in its lifecycle.</summary>
    public BlobStatus Status { get; private set; }

    /// <summary>Gets when the retention window started, or null when the blob is not a candidate.</summary>
    public DateTimeOffset? OrphanSince { get; private set; }

    /// <summary>Gets every place this content lives.</summary>
    public IReadOnlyList<BlobLocation> Locations => _locations.AsReadOnly();

    /// <summary>Gets a value indicating whether the content has been removed from every backend.</summary>
    public bool IsPurged => Status == BlobStatus.Purged;

    /// <summary>Gets the id of the location reads are served from, or null once the blob is purged.</summary>
    /// <remarks>
    /// A pointer rather than a state on the location, because "exactly one" is a cardinality and a
    /// cardinality belongs in the type of a column. Spread over rows it needs an index to forbid the
    /// second one, an ordering of the two writes to satisfy that index, and every future call site to
    /// remember the ordering. A column holding one value needs none of the three.
    /// </remarks>
    public BlobLocationId? PrimaryLocationId { get; private set; }

    /// <summary>Gets the location reads are served from, or null once the blob is purged.</summary>
    public BlobLocation? Primary =>
        PrimaryLocationId is { } id ? _locations.Find(l => l.Id == id) : null;

    /// <summary>
    /// Gets the readable locations, best first: the primary, then verified replicas, then legacy
    /// copies. This is the order a resolver should try, and it is what makes a failed read fall
    /// back instead of surfacing as an error.
    /// </summary>
    public IReadOnlyList<BlobLocation> ReadOrder =>
        [.. _locations
            .Where(l => l.IsReadable)
            .OrderBy(l => l.Id == PrimaryLocationId ? 0
                        : l.State == LocationState.Replica ? 1
                        : 2)];

    /// <summary>Registers content for the first time, together with the location holding it.</summary>
    /// <param name="id">The digest of the content.</param>
    /// <param name="sizeBytes">The size of the content, in bytes.</param>
    /// <param name="provider">The storage backend the content was written to.</param>
    /// <param name="objectKey">The address within that backend.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The newly registered blob.</returns>
    /// <remarks>
    /// A location is mandatory: content with no place to read it from is not content. The initial
    /// location is the one reads go to because it is, by construction, the only one. It is recorded
    /// verified: the caller has just written these bytes, and the blob's own identifier is their
    /// digest — the same claim <see cref="AddLocation"/> makes for a copy written synchronously.
    /// </remarks>
    public static Blob Register(
        BlobId id,
        long sizeBytes,
        StorageProvider provider,
        ObjectKey objectKey,
        DateTimeOffset now,
        Actor by)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(objectKey);
        ArgumentNullException.ThrowIfNull(by);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        var blob = new Blob(id, sizeBytes, now, by);

        var location = new BlobLocation(
            BlobLocationId.New(), id, provider, objectKey, LocationState.Replica, now);

        location.Verify(now);

        blob._locations.Add(location);
        blob.PrimaryLocationId = location.Id;

        blob.RaiseDomainEvent(new BlobRegistered(
            id.Value, sizeBytes, location.Id.Value,
            provider.Name, objectKey.Bucket, objectKey.Key, by.Value, now));

        return blob;
    }

    /// <summary>Registers a copy of the content on another backend.</summary>
    /// <param name="provider">The backend receiving the copy.</param>
    /// <param name="objectKey">The address within that backend.</param>
    /// <param name="copyInFlight">
    /// True while the copy is still being written, false when it is already complete and verified.
    /// </param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The location that was registered.</returns>
    public BlobLocation AddLocation(
        StorageProvider provider,
        ObjectKey objectKey,
        bool copyInFlight,
        DateTimeOffset now,
        Actor by)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(objectKey);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));

        var exists = _locations.Exists(l => l.Provider == provider && l.ObjectKey == objectKey);
        CheckRule(new LocationMustNotAlreadyExistRule(provider, exists));

        var state = copyInFlight ? LocationState.Migrating : LocationState.Replica;
        var location = new BlobLocation(BlobLocationId.New(), Id, provider, objectKey, state, now);

        if (!copyInFlight)
            location.Verify(now);

        _locations.Add(location);
        Touch(now, by);

        RaiseDomainEvent(new BlobLocationAdded(
            Id.Value, location.Id.Value, provider.Name,
            objectKey.Bucket, objectKey.Key, state.Code, now));

        return location;
    }

    /// <summary>Records that a copy has been confirmed to match the blob's digest.</summary>
    /// <param name="locationId">The location that was verified.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void VerifyLocation(BlobLocationId locationId, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));

        var location = _locations.Find(l => l.Id == locationId);
        CheckRule(new LocationMustBelongToBlobRule(Id, location is not null, locationId));

        if (location!.Id == PrimaryLocationId)
            return;

        location.Verify(now);
        Touch(now, by);

        RaiseDomainEvent(new BlobLocationVerified(
            Id.Value, location.Id.Value, location.Provider.Name, now));
    }

    /// <summary>Switches reads to another location, demoting the current one to legacy.</summary>
    /// <param name="locationId">The location that should serve reads.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// Moving the pointer is the switch; the two locations only record that one is now current and
    /// the other superseded. Because the pointer is a single column, the blob cannot be left without
    /// a location serving reads or with two, whatever order the writes reach the database in. This is
    /// the single step that completes a provider migration, and a rollback is the same call in the
    /// other direction.
    /// </remarks>
    public void PromoteToPrimary(BlobLocationId locationId, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));

        var target = _locations.Find(l => l.Id == locationId);
        CheckRule(new LocationMustBelongToBlobRule(Id, target is not null, locationId));
        CheckRule(new LocationMustBeVerifiedBeforePromotionRule(target!.State));

        if (target.Id == PrimaryLocationId)
            return;

        var previous = Primary;

        previous?.ChangeState(LocationState.Legacy);
        target.ChangeState(LocationState.Replica);
        PrimaryLocationId = target.Id;
        Touch(now, by);

        RaiseDomainEvent(new BlobPrimarySwitched(
            Id.Value,
            previous?.Id.Value,
            target.Id.Value,
            previous?.Provider.Name,
            target.Provider.Name,
            now));
    }

    /// <summary>Removes a superseded location from the blob.</summary>
    /// <param name="locationId">The location to remove.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// Removing the record is not deleting the object. The event carries the address so a consumer
    /// can perform the physical delete — and so an operator can find the object if it does not.
    /// Only a superseded location can go: promote another one first, which is the order a provider
    /// migration already follows.
    /// </remarks>
    public void RemoveLocation(BlobLocationId locationId, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));

        var location = _locations.Find(l => l.Id == locationId);
        CheckRule(new LocationMustBelongToBlobRule(Id, location is not null, locationId));

        var readableLeft = _locations.Count(l => l.IsReadable && l.Id != locationId);
        CheckRule(new BlobMustKeepAReadableLocationRule(readableLeft == 0));
        CheckRule(new LocationMustNotBeServingReadsRule(PrimaryLocationId, locationId));

        _locations.Remove(location!);
        Touch(now, by);

        RaiseDomainEvent(new BlobLocationRemoved(
            Id.Value, location!.Id.Value, location.Provider.Name,
            location.ObjectKey.Bucket, location.ObjectKey.Key, now));
    }

    /// <summary>Opens the retention window after no live reference was found.</summary>
    /// <param name="hasLiveReferences">Whether a live document version still points at this content.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// The reference fact is supplied by the caller: documents are separate aggregates that a blob
    /// cannot see. A stored counter is deliberately avoided — it drifts on the first incident, and
    /// once nobody trusts it, nobody dares purge anything again.
    /// </remarks>
    public void MarkOrphanCandidate(bool hasLiveReferences, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));
        CheckRule(new BlobMustHaveNoLiveReferencesRule(hasLiveReferences));

        if (Status == BlobStatus.OrphanCandidate)
            return;

        Status = BlobStatus.OrphanCandidate;
        OrphanSince = now;
        Touch(now, by);

        RaiseDomainEvent(new BlobMarkedOrphanCandidate(Id.Value, now, now));
    }

    /// <summary>Closes the retention window because a live reference appeared again.</summary>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// This is why a collector must re-check immediately before purging: between the two passes, a
    /// new upload of identical content reuses this very blob, and the window must close rather than
    /// the content disappear under the new document.
    /// </remarks>
    public void Reactivate(DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));

        if (Status == BlobStatus.Active)
            return;

        Status = BlobStatus.Active;
        OrphanSince = null;
        Touch(now, by);

        RaiseDomainEvent(new BlobReactivated(Id.Value, now));
    }

    /// <summary>Brings a purged digest back at the address its bytes have been written to again.</summary>
    /// <param name="provider">The storage backend the content was written to.</param>
    /// <param name="objectKey">The address within that backend.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// <para>
    /// The identifier is the content, so the same file uploaded after a purge resolves to this row
    /// rather than to a new one — and the row is kept for audit, so it cannot be registered a
    /// second time either. Something has to bring it back into service, and it cannot be
    /// <see cref="Reactivate"/>: <see cref="MarkPurged"/> cleared every location, and a blob with
    /// no location has no read path whatever its status says.
    /// </para>
    /// <para>
    /// That is why this takes an address rather than nothing. The caller has already written the
    /// bytes; restoring records where they are, which is the only claim that makes the revived row
    /// true. The size is not a parameter for the same reason the digest is the identifier — the
    /// content is identical by definition.
    /// </para>
    /// </remarks>
    public void Restore(
        StorageProvider provider, ObjectKey objectKey, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(objectKey);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustBePurgedToRestoreRule(Status));

        var location = new BlobLocation(
            BlobLocationId.New(), Id, provider, objectKey, LocationState.Replica, now);

        location.Verify(now);

        _locations.Add(location);
        PrimaryLocationId = location.Id;

        Status = BlobStatus.Active;
        OrphanSince = null;
        Touch(now, by);

        RaiseDomainEvent(new BlobRestored(
            Id.Value, SizeBytes, location.Id.Value,
            provider.Name, objectKey.Bucket, objectKey.Key, by.Value, now));
    }

    /// <summary>Records that the content has been removed from every backend.</summary>
    /// <param name="hasLiveReferences">
    /// Whether a live document version still points at this content, re-checked immediately before
    /// the purge.
    /// </param>
    /// <param name="retentionCutoff">
    /// The instant before which an orphan candidate is old enough to purge. Supplied by the caller
    /// so the retention policy lives in configuration while the rule lives in the domain.
    /// </param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// Records a removal that already happened rather than requesting one — the domain has no way
    /// to delete a byte, and that is the property the whole storage model rests on. Terminal for
    /// every transition but <see cref="Restore"/>, which is what the same content uploaded again
    /// resolves to.
    /// </remarks>
    public void MarkPurged(
        bool hasLiveReferences, DateTimeOffset retentionCutoff, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new BlobMustNotBePurgedRule(Status));
        CheckRule(new BlobMustHaveNoLiveReferencesRule(hasLiveReferences));
        CheckRule(new BlobMustBeAnAgedOrphanToPurgeRule(Status, OrphanSince, retentionCutoff));

        var orphanSince = OrphanSince!.Value;

        Status = BlobStatus.Purged;
        _locations.Clear();
        PrimaryLocationId = null;
        Touch(now, by);

        RaiseDomainEvent(new BlobPurged(Id.Value, SizeBytes, orphanSince, by.Value, now));
    }
}
