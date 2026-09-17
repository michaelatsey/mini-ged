namespace MicroKit.Domain.Events;

/// <summary>
/// Abstract base for domain events. Declare concrete events as sealed positional records
/// that repeat <paramref name="OccurredAt"/> as their last parameter and forward it to
/// this base.
/// </summary>
/// <param name="OccurredAt">
/// The instant the business fact occurred, in UTC. Pass the same value to every event raised
/// within one business operation so consumers can group and order them reliably.
/// </param>
/// <remarks>
/// <para>
/// The instant is a positional parameter with no default. It is an input of the operation
/// that produced the fact, not something the event discovers on its own: a
/// <c>DateTimeOffset.UtcNow</c> default would make the value optional at every construction
/// site, and events belonging to one transaction would drift apart by milliseconds with
/// nothing to signal it. Declaring it positionally moves that failure to the compiler while
/// keeping concrete events to a single line.
/// </para>
/// <para>
/// <see cref="EventId"/> is not positional because it carries no business meaning — it only
/// has to be unique, and a default keeps event declarations free of ceremony. It uses a UUID
/// version 7 (time-ordered) value: domain events are routinely persisted in an outbox keyed
/// by this identifier, and a time-ordered key keeps the index compact and makes ordering by
/// identifier meaningful. The <c>init</c> accessor stays public so that replay, import and
/// migration can preserve an original identity, which is what makes downstream deduplication
/// work.
/// </para>
/// <para>
/// Concrete events should carry primitive members rather than value objects: events outlive
/// deployments in message stores, and a value object refactoring must not invalidate messages
/// already in flight.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed record OrderShipped(
///     Guid OrderId, string TrackingNumber, DateTimeOffset OccurredAt)
///     : DomainEvent(OccurredAt);
///
/// // raising it
/// RaiseDomainEvent(new OrderShipped(Id.Value, tracking.Value, now));
///
/// // re-materializing an existing event, preserving its identity
/// var replayed = new OrderShipped(id, tracking, original.OccurredAt)
/// {
///     EventId = original.EventId
/// };
/// </code>
/// </example>
public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>
    /// Gets the unique, time-ordered identifier for this domain event.
    /// </summary>
    public Guid EventId { get; init; } = Guid.CreateVersion7();
}
