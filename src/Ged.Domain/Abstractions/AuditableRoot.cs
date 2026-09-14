namespace Ged.Domain.Abstractions;

/// <summary>
/// Base class for aggregate roots that carry audit metadata supplied by the caller.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type.</typeparam>
/// <remarks>
/// <para>
/// The creation instant and the acting identity are inputs of the business operation, not
/// values the aggregate discovers on its own. They travel through the factory method that
/// creates the aggregate, which keeps it deterministic and testable against a fixed clock.
/// </para>
/// <para>
/// This type is used instead of <c>MicroKit.Domain.Aggregates.AuditableAggregateRoot</c>,
/// whose constructor reads <c>DateTimeOffset.UtcNow</c> internally and exposes independent
/// setters for the update fields. See <c>docs/microkit-deviations.md</c> for the full list
/// and the conditions under which this shim can be deleted.
/// </para>
/// </remarks>
public abstract class AuditableRoot<TId> : AggregateRoot<TId>
    where TId : IEntityId
{
    /// <summary>Initializes a new auditable aggregate root.</summary>
    /// <param name="id">The identifier of this aggregate.</param>
    /// <param name="createdAt">The instant this aggregate was created, in UTC.</param>
    /// <param name="createdBy">The actor creating this aggregate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="createdBy"/> is null.</exception>
    protected AuditableRoot(TId id, DateTimeOffset createdAt, Actor createdBy) : base(id)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>Gets the instant this aggregate was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the actor that created this aggregate.</summary>
    public Actor CreatedBy { get; }

    /// <summary>Gets the instant of the last modification, or null when never modified.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Gets the actor of the last modification, or null when never modified.</summary>
    public Actor? UpdatedBy { get; private set; }

    /// <summary>
    /// Records that this aggregate was modified, updating both audit fields together.
    /// </summary>
    /// <param name="updatedAt">The instant of the modification, in UTC.</param>
    /// <param name="updatedBy">The actor performing the modification.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updatedBy"/> is null.</exception>
    /// <remarks>
    /// Routing both fields through a single member is what prevents them from drifting apart:
    /// with independent setters it is possible to update the timestamp and forget the actor,
    /// producing an audit trail that records when something changed but not who changed it.
    /// </remarks>
    protected void Touch(DateTimeOffset updatedAt, Actor updatedBy)
    {
        ArgumentNullException.ThrowIfNull(updatedBy);

        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }
}
