using System.Globalization;
using Ged.Domain.Blobs;

namespace Ged.Features.Blobs.Jobs;

/// <summary>Removes content whose retention window has elapsed.</summary>
/// <param name="connections">Finds purgeable candidates and re-checks references.</param>
/// <param name="blobs">Loads each candidate as an aggregate.</param>
/// <param name="storages">Removes the bytes from every backend holding them.</param>
/// <param name="unitOfWork">Commits each purge.</param>
/// <param name="clock">Supplies the instant of the purge and the retention cutoff.</param>
/// <remarks>
/// <para>
/// The only code in the system that deletes a byte, and it runs behind three guards: the window must
/// have elapsed, no live version may reference the content, and the reference check is repeated here
/// rather than trusted from the marking pass.
/// </para>
/// <para>
/// That repetition is not caution for its own sake. Between the two passes, a new upload of identical
/// content resolves to the same digest and reuses the very same blob — so a collector that trusted a
/// status set days earlier would delete content out from under a live document.
/// </para>
/// <para>
/// The row is marked before storage is cleared, and that order is the second guard doing real work.
/// The mark is the only step that carries the blob's concurrency token, so it is the only step that
/// can notice an upload deduplicating against this very digest at this very moment: the commit
/// fails and no byte is touched. Clearing storage first would put the irreversible step ahead of
/// the detection, and the conflict would arrive once the content was already gone.
/// </para>
/// <para>
/// The cost is a leak rather than a loss, and it is paid by anything that stops the deletes from
/// happening once the row is committed: the process dying, or a backend refusing the removal. The
/// row is terminal, so no later pass retries — the objects simply remain with no location row
/// pointing at them, which is exactly the shape <see cref="ReconcileStorageJob"/> reports as
/// orphaned in storage. Losing the bytes a live document points at is not recoverable that way,
/// which is why the order is this one.
/// </para>
/// </remarks>
public sealed class PurgeOrphanBlobsJob(
    IDbConnectionFactory connections,
    IBlobRepository blobs,
    IObjectStorageRegistry storages,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string CandidatesSql = """
        SELECT      id AS id
        FROM        blob
        WHERE       status = 'ORPHAN_CANDIDATE'
          AND       orphan_since <= @cutoff
        ORDER BY    orphan_since
        OFFSET      0 ROWS
        FETCH FIRST @take ROWS ONLY
        """;

    private const string StatusSql = """
        SELECT status
        FROM   blob
        WHERE  id = @blobId
        """;

    private const string StillReferencedSql = """
        SELECT COUNT(*)
        FROM   document_version v
        INNER JOIN document d ON d.id = v.document_id
        WHERE  v.blob_id = @blobId
          AND  d.deleted_at IS NULL
        """;

    /// <summary>Runs one batch.</summary>
    /// <param name="retention">How long a candidate must wait before it can be purged.</param>
    /// <param name="batchSize">How many candidates to consider.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>What the batch did.</returns>
    public async Task<PurgeReport> RunAsync(
        TimeSpan retention, int batchSize = 200, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var cutoff = now - retention;

        var candidates = await ReadCandidatesAsync(cutoff, batchSize, ct);

        var purged = 0;
        var reactivated = 0;
        var contended = 0;

        foreach (var digest in candidates)
        {
            var blobId = BlobId.FromSha256(digest);
            var blob = await blobs.FindAsync(blobId, ct);

            if (blob is null || blob.IsPurged)
                continue;

            var stillReferenced = await IsStillReferencedAsync(digest, ct);

            if (stillReferenced)
            {
                blob.Reactivate(now, Actor.System);
                await unitOfWork.CommitAsync(ct);
                reactivated++;

                continue;
            }

            // Read before the transition: marking the blob purged clears its locations, and they
            // are the only record of where the bytes are.
            var addresses = blob.Locations
                .Select(l => (l.Provider, l.ObjectKey))
                .ToArray();

            blob.MarkPurged(hasLiveReferences: false, cutoff, now, Actor.System);

            try
            {
                await unitOfWork.CommitAsync(ct);
            }
            catch (PersistenceException)
            {
                // Two different things fail this commit, and only the row can say which. Either
                // another transaction changed the blob — an upload deduplicating against this
                // digest is the case this exists for — or the write itself failed. The first is
                // expected and the next pass reconsiders the blob; the second must not be reported
                // as a healthy batch by the one job in the system that deletes bytes.
                unitOfWork.DiscardChanges();

                if (await ReadStatusAsync(digest, ct) == BlobStatus.OrphanCandidate)
                    throw;

                contended++;

                continue;
            }

            // The row says purged and its locations are gone, but the bytes are not: this is the
            // one moment at which a re-upload of the same content resolves to the same
            // content-addressed address. Reading the status back narrows that window to a single
            // round-trip — it does not close it, and a clobbered object surfaces as a location
            // missing from storage in the next reconciliation pass.
            if (await ReadStatusAsync(digest, ct) != BlobStatus.Purged)
            {
                contended++;

                continue;
            }

            foreach (var (provider, objectKey) in addresses)
            {
                if (storages.TryGet(provider, out var storage) && storage is not null)
                    await storage.DeleteAsync(objectKey, ct);
            }

            purged++;
        }

        return new PurgeReport(candidates.Count, purged, reactivated, contended);
    }

    private async Task<IReadOnlyList<string>> ReadCandidatesAsync(
        DateTimeOffset cutoff, int batchSize, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = CandidatesSql;
        command.AddParameter("cutoff", cutoff);
        command.AddParameter("take", Math.Clamp(batchSize, 1, 1000));

        var ids = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetString("id"));

        return ids;
    }

    /// <summary>Reads what the blob row says now, which is the only authority on a lost race.</summary>
    /// <param name="digest">The blob to read.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The status, or null when the row is gone.</returns>
    private async Task<BlobStatus?> ReadStatusAsync(string digest, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = StatusSql;
        command.AddParameter("blobId", digest);

        var code = await command.ExecuteScalarAsync(ct) as string;

        return code is null ? null : BlobStatus.From(code);
    }

    private async Task<bool> IsStillReferencedAsync(string digest, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = StillReferencedSql;
        command.AddParameter("blobId", digest);

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0;
    }
}

/// <summary>What one purge batch did.</summary>
/// <param name="Considered">How many candidates were examined.</param>
/// <param name="Purged">How many were removed.</param>
/// <param name="Reactivated">How many were referenced again and spared.</param>
/// <param name="Contended">
/// How many kept their bytes because the blob changed while the job was working on it. Not an
/// error: it is what the reordering above is for, and the next pass reconsiders each of them.
/// </param>
public sealed record PurgeReport(int Considered, int Purged, int Reactivated, int Contended);
