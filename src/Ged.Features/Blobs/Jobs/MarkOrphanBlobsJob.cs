namespace Ged.Features.Blobs.Jobs;

/// <summary>Opens the retention window on content nothing references any more.</summary>
/// <param name="connections">Opens a connection for the set-based update.</param>
/// <param name="clock">Supplies the instant the window opens.</param>
/// <remarks>
/// <para>
/// Written as one statement rather than as a loop over aggregates. Loading four million blobs to
/// change a column would exhaust memory long before it finished, and the work has no invariant to
/// protect: marking a candidate decides nothing, it only starts a clock.
/// </para>
/// <para>
/// Nothing is removed here, and nothing is removed by the next pass either unless the window has
/// elapsed and the check still holds. That is what makes an accidental deletion recoverable and a
/// bug in this job an incident rather than a data loss.
/// </para>
/// </remarks>
public sealed class MarkOrphanBlobsJob(IDbConnectionFactory connections, IClock clock)
{
    private const string Sql = """
        UPDATE blob
        SET    status       = 'ORPHAN_CANDIDATE',
               orphan_since = @now
        WHERE  status = 'ACTIVE'
          AND  NOT EXISTS (
                SELECT 1
                FROM   document_version v
                INNER JOIN document d ON d.id = v.document_id
                WHERE  v.blob_id = blob.id
                  AND  d.deleted_at IS NULL)
        """;

    /// <summary>Runs the pass.</summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>How many blobs entered the retention window.</returns>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = Sql;
        command.AddParameter("now", clock.UtcNow);

        return await command.ExecuteNonQueryAsync(ct);
    }
}
