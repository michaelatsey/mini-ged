using Ged.Domain.Blobs;

namespace Ged.Features.Blobs.Jobs;

/// <summary>Compares what the database believes with what each backend actually holds.</summary>
/// <param name="connections">Reads the known locations.</param>
/// <param name="storages">Walks the backends.</param>
/// <remarks>
/// <para>
/// Reconciliation runs in both directions, because each direction finds a different kind of damage.
/// </para>
/// <para>
/// Database to storage finds <em>silent corruption</em>: a location the application will happily
/// serve until a user clicks and gets nothing. Storage to database finds <em>orphans</em>: objects
/// written by an upload whose transaction then failed, which nothing will ever reference or reclaim.
/// </para>
/// <para>
/// The job reports and never repairs. Deleting an unreferenced object automatically would be
/// indistinguishable from deleting content whose row simply has not been committed yet.
/// </para>
/// </remarks>
public sealed class ReconcileStorageJob(
    IDbConnectionFactory connections, IObjectStorageRegistry storages)
{
    private const string LocationsSql = """
        SELECT provider   AS provider,
               bucket     AS bucket,
               object_key AS object_key,
               blob_id    AS blob_id
        FROM   blob_location
        WHERE  provider = @provider
        """;

    /// <summary>Runs a reconciliation pass against one backend.</summary>
    /// <param name="provider">The backend to walk.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>What was found.</returns>
    public async Task<ReconciliationReport> RunAsync(
        StorageProvider provider, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var storage = storages.Resolve(provider);
        var known = await ReadKnownAsync(provider, ct);

        var present = new HashSet<string>(StringComparer.Ordinal);
        var orphans = new List<string>();

        await foreach (var key in storage.ListAsync(ct))
        {
            present.Add(key.ToString());

            if (!known.Contains(key.ToString()))
                orphans.Add(key.ToString());
        }

        var missing = known.Where(k => !present.Contains(k)).ToArray();

        return new ReconciliationReport(provider.Name, known.Count, present.Count, missing, orphans);
    }

    private async Task<HashSet<string>> ReadKnownAsync(
        StorageProvider provider, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        command.CommandText = LocationsSql;
        command.AddParameter("provider", provider.Name);

        var known = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            known.Add($"{reader.GetString("bucket")}/{reader.GetString("object_key")}");

        return known;
    }
}

/// <summary>What one reconciliation pass found.</summary>
/// <param name="Provider">The backend walked.</param>
/// <param name="KnownLocations">How many locations the database records.</param>
/// <param name="ObjectsInStorage">How many objects the backend holds.</param>
/// <param name="MissingFromStorage">
/// Locations the database records but the backend does not hold. Silent corruption: reads will fail.
/// </param>
/// <param name="OrphanedInStorage">
/// Objects the backend holds that no location records. Wasted space, and a sign an upload's
/// transaction failed after its write succeeded.
/// </param>
public sealed record ReconciliationReport(
    string Provider,
    int KnownLocations,
    int ObjectsInStorage,
    IReadOnlyList<string> MissingFromStorage,
    IReadOnlyList<string> OrphanedInStorage);
