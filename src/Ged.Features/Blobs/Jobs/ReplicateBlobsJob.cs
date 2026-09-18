using Ged.Domain.Blobs;


namespace Ged.Features.Blobs.Jobs;

/// <summary>Copies content to another backend and switches reads over to it.</summary>
/// <param name="connections">Finds blobs that have no copy on the target yet.</param>
/// <param name="blobs">Loads each blob as an aggregate.</param>
/// <param name="storages">Reads from the source and writes to the target.</param>
/// <param name="resolver">Serves the content from whichever location can.</param>
/// <param name="unitOfWork">Commits each step.</param>
/// <param name="clock">Supplies the instant of each transition.</param>
/// <remarks>
/// <para>
/// This job is the whole reason content is addressed by digest rather than by path: changing storage
/// provider is a sequence of state transitions on data, with no code change anywhere else and a
/// two-step rollback.
/// </para>
/// <code>
/// AddLocation(target, copyInFlight: true)  -> MIGRATING   copy in flight
/// copy the bytes
/// VerifyLocation(id)                       -> REPLICA     digest confirmed
/// PromoteToPrimary(id)                     -> pointer     reads switch, old becomes LEGACY
/// </code>
/// <para>
/// The copy is verified before it can serve reads. Promoting an unverified copy would point every
/// read at bytes nobody has checked — the one failure a content-addressed model exists to prevent.
/// </para>
/// </remarks>
public sealed class ReplicateBlobsJob(
    IDbConnectionFactory connections,
    IBlobRepository blobs,
    IObjectStorageRegistry storages,
    IBlobLocationResolver resolver,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string PendingSql = """
        SELECT      b.id AS id
        FROM        blob b
        WHERE       b.status <> 'PURGED'
          AND       NOT EXISTS (
                      SELECT 1 FROM blob_location l
                      WHERE  l.blob_id = b.id AND l.provider = @target)
        ORDER BY    b.created_at
        OFFSET      0 ROWS
        FETCH FIRST @take ROWS ONLY
        """;

    /// <summary>Copies a batch of blobs to the target backend.</summary>
    /// <param name="target">The backend to copy to.</param>
    /// <param name="promote">Whether to switch reads to the new copy once verified.</param>
    /// <param name="batchSize">How many blobs to copy.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>What the batch did.</returns>
    public async Task<ReplicationReport> RunAsync(
        StorageProvider target, bool promote, int batchSize = 100, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var storage = storages.Resolve(target);
        var pending = await ReadPendingAsync(target, batchSize, ct);

        var copied = 0;
        var promoted = 0;
        var failed = 0;

        foreach (var digest in pending)
        {
            var blob = await blobs.FindAsync(BlobId.FromSha256(digest), ct);

            if (blob is null || blob.IsPurged)
                continue;

            try
            {
                var now = clock.UtcNow;
                var key = storage.KeyFor(digest);

                var location = blob.AddLocation(target, key, copyInFlight: true, now, Actor.System);
                await unitOfWork.CommitAsync(ct);

                await using (var content = await resolver.OpenAsync(blob, ct))
                {
                    await storage.PutAsync(
                        key, content.Stream, new ObjectMetadata(blob.SizeBytes, null), ct);
                }

                blob.VerifyLocation(location.Id, clock.UtcNow, Actor.System);

                if (promote)
                {
                    blob.PromoteToPrimary(location.Id, clock.UtcNow, Actor.System);
                    promoted++;
                }

                await unitOfWork.CommitAsync(ct);
                copied++;
            }
            catch (ObjectNotFoundException)
            {
                // The source copy is gone. The location stays MIGRATING, which is not readable, so
                // nothing is served from it; reconciliation reports it for repair.
                failed++;
            }
        }

        return new ReplicationReport(pending.Count, copied, promoted, failed);
    }

    private async Task<IReadOnlyList<string>> ReadPendingAsync(
        StorageProvider target, int batchSize, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = PendingSql;
        command.AddParameter("target", target.Name);
        command.AddParameter("take", Math.Clamp(batchSize, 1, 1000));

        var ids = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetString("id"));

        return ids;
    }
}

/// <summary>What one replication batch did.</summary>
/// <param name="Considered">How many blobs had no copy on the target.</param>
/// <param name="Copied">How many were copied and verified.</param>
/// <param name="Promoted">How many now serve reads from the target.</param>
/// <param name="Failed">How many could not be read from any existing location.</param>
public sealed record ReplicationReport(int Considered, int Copied, int Promoted, int Failed);
