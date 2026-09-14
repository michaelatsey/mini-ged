namespace Ged.Adapters.Persistence.Outbox;

/// <summary>
/// A domain event captured in the same transaction as the state change that produced it.
/// </summary>
/// <remarks>
/// <para>
/// Infrastructure, not domain. Publishing after the commit loses the event when the broker is
/// unavailable; publishing before it emits an event describing a change that never happened.
/// Writing the row inside the transaction makes the two outcomes impossible, and a dispatcher
/// delivers afterwards with retries.
/// </para>
/// <para>
/// The payload is stored as JSON rather than as a serialized object graph so it stays readable in
/// a query and survives a refactoring of the event type — a message already in flight must not be
/// invalidated by a deployment.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>Gets the message identifier. Time-ordered, so the index stays compact.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the instant the business fact occurred, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Gets the assembly-qualified-free full name of the event type.</summary>
    public string Type { get; init; } = null!;

    /// <summary>Gets the serialized event.</summary>
    public string Payload { get; init; } = null!;

    /// <summary>Gets when the message was successfully dispatched, or null while pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Gets how many delivery attempts have been made.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets the last delivery error, or null.</summary>
    public string? Error { get; set; }
}
