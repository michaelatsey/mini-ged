namespace Ged.Domain.Abstractions;

/// <summary>
/// Base record for every domain event raised in this solution.
/// </summary>
/// <remarks>
/// <para>
/// This type exists to make the instant a mandatory constructor argument.
/// <see cref="DomainEvent"/> defaults its own <c>OccurredAt</c> to <c>DateTimeOffset.UtcNow</c>,
/// which makes the value optional at every construction site: events belonging to a single
/// business operation then drift apart by milliseconds with nothing to signal it. Requiring it
/// here moves that failure to the compiler.
/// </para>
/// <para>
/// Concrete events are sealed positional records that repeat <c>OccurredAt</c> as their last
/// parameter and forward it to this base:
/// <code>
/// public sealed record DocumentRenamed(
///     Guid DocumentId, string PreviousName, string NewName, DateTimeOffset OccurredAt)
///     : GedDomainEvent(OccurredAt);
/// </code>
/// The parameter reuses the inherited property rather than declaring a new one, so the event
/// exposes a single <c>OccurredAt</c>.
/// </para>
/// <para>
/// Events should carry primitive members rather than value objects: they outlive deployments in
/// message stores, and a value object refactoring must not invalidate messages already in flight.
/// </para>
/// </remarks>
public abstract record GedDomainEvent : DomainEvent
{
    /// <summary>Initializes a new domain event stamped with the supplied instant.</summary>
    /// <param name="occurredAt">
    /// The instant the business fact occurred, in UTC. Pass the same value to every event raised
    /// within one business operation so consumers can group and order them reliably.
    /// </param>
    protected GedDomainEvent(DateTimeOffset occurredAt) => OccurredAt = occurredAt;
}
