namespace Ged.Domain.Blobs.Rules;

/// <summary>Prevents any mutation of a purged blob.</summary>
/// <param name="status">The blob's current status.</param>
/// <remarks>
/// Purging is terminal. Once the bytes are gone from every backend there is nothing left for a
/// state transition to describe, and allowing one would produce a record that claims content
/// exists when it does not.
/// </remarks>
public sealed class BlobMustNotBePurgedRule(BlobStatus status) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => status == BlobStatus.Purged;

    /// <inheritdoc />
    public override string Message => "A purged blob cannot be modified.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [status];
}

/// <summary>Prevents registering the same address twice for one blob.</summary>
/// <param name="provider">The storage backend.</param>
/// <param name="alreadyRegistered">Whether that address is already registered.</param>
public sealed class LocationMustNotAlreadyExistRule(StorageProvider provider, bool alreadyRegistered)
    : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => alreadyRegistered;

    /// <inheritdoc />
    public override string Message =>
        $"This blob already has a location on provider '{provider}' at that address.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [provider, alreadyRegistered];
}

/// <summary>Ensures a location belongs to the blob it is being addressed on.</summary>
/// <param name="blobId">The blob.</param>
/// <param name="known">Whether the location was found on the blob.</param>
/// <param name="candidate">The location being addressed.</param>
public sealed class LocationMustBelongToBlobRule(BlobId blobId, bool known, BlobLocationId candidate)
    : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => !known;

    /// <inheritdoc />
    public override string Message => $"Location {candidate} does not belong to blob {blobId}.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [blobId, candidate];
}

/// <summary>
/// Prevents an unverified copy from becoming the location reads are served from.
/// </summary>
/// <param name="state">The candidate location's current state.</param>
/// <remarks>
/// A copy in flight has no guarantee of matching the blob's digest. Promoting it directly would
/// point every read at bytes nobody has checked — the one failure mode a content-addressed model
/// exists to prevent.
/// </remarks>
public sealed class LocationMustBeVerifiedBeforePromotionRule(LocationState state) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => state == LocationState.Migrating;

    /// <inheritdoc />
    public override string Message =>
        "A location must be verified before it can serve reads.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [state];
}

/// <summary>Prevents removing the last readable location of a live blob.</summary>
/// <param name="wouldLeaveNoReadableLocation">Whether removal would leave nothing to read from.</param>
public sealed class BlobMustKeepAReadableLocationRule(bool wouldLeaveNoReadableLocation)
    : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => wouldLeaveNoReadableLocation;

    /// <inheritdoc />
    public override string Message =>
        "Removing this location would leave the blob with no readable copy.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [wouldLeaveNoReadableLocation];
}

/// <summary>Prevents marking a blob orphaned while something still references it.</summary>
/// <param name="hasLiveReferences">Whether at least one live document version still points at it.</param>
/// <remarks>
/// The fact comes from outside: documents are separate aggregates and a blob cannot see them. The
/// caller counts, the rule decides — which keeps the decision in the domain even though the data
/// is not. A stored reference counter is deliberately avoided: it drifts on the first incident and
/// then nobody dares purge anything again.
/// </remarks>
public sealed class BlobMustHaveNoLiveReferencesRule(bool hasLiveReferences) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() => hasLiveReferences;

    /// <inheritdoc />
    public override string Message => "This content is still referenced by a live document version.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [hasLiveReferences];
}

/// <summary>Restricts purging to a blob that has been an orphan candidate long enough.</summary>
/// <param name="status">The blob's current status.</param>
/// <param name="orphanSince">When the blob was marked as an orphan candidate.</param>
/// <param name="cutoff">The instant before which a candidate is old enough to purge.</param>
/// <remarks>
/// The retention window is what makes an accidental deletion recoverable and a bug in the collector
/// an incident rather than a data loss. The cutoff is supplied by the caller so the policy lives in
/// configuration while the rule lives here.
/// </remarks>
public sealed class BlobMustBeAnAgedOrphanToPurgeRule(
    BlobStatus status, DateTimeOffset? orphanSince, DateTimeOffset cutoff) : BusinessRule
{
    /// <inheritdoc />
    public override bool IsBroken() =>
        status != BlobStatus.OrphanCandidate || orphanSince is null || orphanSince > cutoff;

    /// <inheritdoc />
    public override string Message =>
        "Only a blob that has been an orphan candidate beyond the retention window can be purged.";

    /// <inheritdoc />
    protected override object?[] GetEqualityComponents() => [status, orphanSince, cutoff];
}
