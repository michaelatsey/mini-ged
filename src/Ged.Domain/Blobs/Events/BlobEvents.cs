namespace Ged.Domain.Blobs.Events;

/// <summary>Raised when content is registered for the first time, with its initial location.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="SizeBytes">The size of the content, in bytes.</param>
/// <param name="LocationId">The initial location.</param>
/// <param name="Provider">The storage backend holding it.</param>
/// <param name="Bucket">The container within that backend.</param>
/// <param name="Key">The object key within that container.</param>
/// <param name="RegisteredBy">The actor that registered the content.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record BlobRegistered(
    string BlobId,
    long SizeBytes,
    Guid LocationId,
    string Provider,
    string Bucket,
    string Key,
    string RegisteredBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised when a copy of the content is registered on another backend.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="LocationId">The new location.</param>
/// <param name="Provider">The storage backend holding the copy.</param>
/// <param name="Bucket">The container within that backend.</param>
/// <param name="Key">The object key within that container.</param>
/// <param name="State">The role the copy starts in.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record BlobLocationAdded(
    string BlobId,
    Guid LocationId,
    string Provider,
    string Bucket,
    string Key,
    string State,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised when a copy has been confirmed to match the blob's digest.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="LocationId">The verified location.</param>
/// <param name="Provider">The storage backend holding it.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record BlobLocationVerified(
    string BlobId,
    Guid LocationId,
    string Provider,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>
/// Raised when reads are switched to a different location.
/// </summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="PreviousLocationId">
/// The location that was serving reads, now legacy, or null when none was.
/// </param>
/// <param name="NewLocationId">The location now serving reads.</param>
/// <param name="PreviousProvider">The backend reads came from, or null when none was serving them.</param>
/// <param name="NewProvider">The backend reads now go to.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
/// <remarks>
/// <para>
/// This is the event a provider migration is built on. Consumers holding cached read URLs must
/// invalidate them; nothing else changes, because the content itself is identical by definition.
/// </para>
/// <para>
/// "None" is null rather than <see cref="Guid.Empty"/> because a consumer resolves the previous
/// location, and <see cref="BlobLocationId.From"/> refuses the empty value — a sentinel would fail
/// on exactly the case it stands for. No transition leaves a blob that is not purged without a
/// location serving reads, so the null comes only from data: a blob whose serving location was
/// removed before reads became a pointer, which the backfill of that pointer could not fill.
/// </para>
/// </remarks>
public sealed record BlobPrimarySwitched(
    string BlobId,
    Guid? PreviousLocationId,
    Guid NewLocationId,
    string? PreviousProvider,
    string NewProvider,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised when a superseded location is removed from the blob.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="LocationId">The location removed.</param>
/// <param name="Provider">The backend it pointed at.</param>
/// <param name="Bucket">The container it pointed at.</param>
/// <param name="Key">The object key it pointed at.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
/// <remarks>
/// Removing the record is not deleting the object. A consumer is responsible for the physical
/// delete, and it carries the address precisely so it can perform it.
/// </remarks>
public sealed record BlobLocationRemoved(
    string BlobId,
    Guid LocationId,
    string Provider,
    string Bucket,
    string Key,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised when no live reference to the content could be found.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="OrphanSince">When the retention window started.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
/// <remarks>
/// A candidate, not a verdict. Nothing is removed, and a new document referencing the same content
/// reactivates the blob before the window elapses.
/// </remarks>
public sealed record BlobMarkedOrphanCandidate(
    string BlobId,
    DateTimeOffset OrphanSince,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised when a live reference appears again before the retention window elapses.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record BlobReactivated(
    string BlobId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised when a purged digest is uploaded again and its row comes back into service.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="SizeBytes">The size of the content, in bytes. Unchanged — the digest is the content.</param>
/// <param name="LocationId">The location the bytes were written to again.</param>
/// <param name="Provider">The storage backend now holding them.</param>
/// <param name="Bucket">The container within that backend.</param>
/// <param name="Key">The object key within that container.</param>
/// <param name="RestoredBy">The actor whose upload brought the content back.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
/// <remarks>
/// Distinct from <see cref="BlobReactivated"/>, which closes a retention window on content that
/// never left. This one says the bytes were gone and are back, which is what a consumer holding a
/// cached "this digest no longer exists" answer needs to hear.
/// </remarks>
public sealed record BlobRestored(
    string BlobId,
    long SizeBytes,
    Guid LocationId,
    string Provider,
    string Bucket,
    string Key,
    string RestoredBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

/// <summary>Raised once the content has been removed from every backend.</summary>
/// <param name="BlobId">The content digest.</param>
/// <param name="SizeBytes">The size that was reclaimed, in bytes.</param>
/// <param name="OrphanSince">When the retention window started.</param>
/// <param name="PurgedBy">The actor — usually a collector — that performed the purge.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
/// <remarks>
/// Records a removal that already happened; it does not request one. The row survives it, and
/// <see cref="BlobRestored"/> is what follows if the same content is ever uploaded again.
/// </remarks>
public sealed record BlobPurged(
    string BlobId,
    long SizeBytes,
    DateTimeOffset OrphanSince,
    string PurgedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
