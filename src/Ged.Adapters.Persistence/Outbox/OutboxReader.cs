using Dapper;
using MicroKit.Persistence.Abstractions;

namespace Ged.Adapters.Persistence.Outbox;

/// <summary>
/// Claims pending outbox messages for delivery, safely across several running instances.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>FOR UPDATE SKIP LOCKED</c>: each instance takes rows nobody else holds and moves on
/// rather than blocking. That is what lets the dispatcher scale horizontally without a distributed
/// lock — and a distributed lock is a component that fails in its own right.
/// </para>
/// <para>
/// Reads go through Dapper rather than the <see cref="GedDbContext"/>. Claiming is set-based work on
/// rows, not aggregate work, and running it through change tracking would load every pending
/// message into memory to update a column.
/// </para>
/// </remarks>
public sealed class OutboxReader(IDbConnectionFactory connections)
{
    private const string ClaimSql = """
        SELECT id          AS Id,
               occurred_at AS OccurredAt,
               type        AS Type,
               payload     AS Payload,
               attempts    AS Attempts
        FROM   outbox_message
        WHERE  processed_at IS NULL
        ORDER  BY occurred_at
        LIMIT  @BatchSize
        FOR UPDATE SKIP LOCKED;
        """;

    private const string MarkProcessedSql = """
        UPDATE outbox_message
        SET    processed_at = @ProcessedAt,
               error        = NULL
        WHERE  id = ANY(@Ids);
        """;

    private const string MarkFailedSql = """
        UPDATE outbox_message
        SET    attempts = attempts + 1,
               error    = @Error
        WHERE  id = @Id;
        """;

    /// <summary>Claims a batch of pending messages.</summary>
    /// <param name="batchSize">The maximum number of messages to claim.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The claimed messages, oldest first.</returns>
    public async Task<IReadOnlyList<PendingMessage>> ClaimAsync(
        int batchSize, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);

        var rows = await connection.QueryAsync<PendingMessage>(
            new CommandDefinition(ClaimSql, new { BatchSize = batchSize }, cancellationToken: ct));

        return [.. rows];
    }

    /// <summary>Marks messages as delivered.</summary>
    /// <param name="ids">The identifiers of the delivered messages.</param>
    /// <param name="processedAt">The instant of delivery, in UTC.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    public async Task MarkProcessedAsync(
        IReadOnlyCollection<Guid> ids, DateTimeOffset processedAt, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return;

        await using var connection = await connections.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            MarkProcessedSql,
            new { Ids = ids.ToArray(), ProcessedAt = processedAt },
            cancellationToken: ct));
    }

    /// <summary>Records a failed delivery attempt.</summary>
    /// <param name="id">The identifier of the message.</param>
    /// <param name="error">The error to record.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    public async Task MarkFailedAsync(Guid id, string error, CancellationToken ct = default)
    {
        await using var connection = await connections.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            MarkFailedSql, new { Id = id, Error = error }, cancellationToken: ct));
    }

    /// <summary>A message awaiting delivery.</summary>
    /// <param name="Id">The message identifier.</param>
    /// <param name="OccurredAt">The instant the business fact occurred.</param>
    /// <param name="Type">The full name of the event type.</param>
    /// <param name="Payload">The serialized event.</param>
    /// <param name="Attempts">How many delivery attempts have already been made.</param>
    public sealed record PendingMessage(
        Guid Id,
        DateTimeOffset OccurredAt,
        string Type,
        string Payload,
        int Attempts);
}
