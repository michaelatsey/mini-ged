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
/// Storage is cleared before the row is marked. If the process dies in between, the blob stays a
/// candidate and the next pass retries; deleting an object twice is harmless, whereas marking first
/// would leave a row claiming content was removed while the bytes remain.
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

            foreach (var location in blob.Locations)
            {
                if (storages.TryGet(location.Provider, out var storage) && storage is not null)
                    await storage.DeleteAsync(location.ObjectKey, ct);
            }

            blob.MarkPurged(hasLiveReferences: false, cutoff, now, Actor.System);
            await unitOfWork.CommitAsync(ct);
            purged++;
        }

        return new PurgeReport(candidates.Count, purged, reactivated);
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
public sealed record PurgeReport(int Considered, int Purged, int Reactivated);
