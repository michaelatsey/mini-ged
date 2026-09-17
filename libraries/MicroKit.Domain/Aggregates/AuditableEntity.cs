using MicroKit.Domain.Identifiers;
using MicroKit.Domain.ValueObjects.Common;

namespace MicroKit.Domain.Aggregates;

/// <summary>
/// Base class for auditable domain entities with identity-based equality and audit tracking.
/// Extends the standard entity behavior with lightweight audit metadata.
/// </summary>
/// <typeparam name="TId">The strongly-typed identifier type</typeparam>
/// <remarks>
/// This class provides audit properties but does not automatically populate them.
/// The Application/Infrastructure layers are responsible for setting audit values
/// during persistence operations based on the current security context.
/// </remarks>
public abstract class AuditableEntity<TId> : Entity<TId>, IAuditableEntity
    where TId : IEntityId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntity{TId}"/> class.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this entity.</param>
    /// <param name="createdAt">The instant this entity was created, in UTC.</param>
    /// <param name="createdBy">
    /// The actor creating this entity. Use <see cref="Actor.System"/> when the action has
    /// no user in context, rather than leaving the creator unattributed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="createdBy"/> is <see langword="null"/>.
    /// </exception>
    protected AuditableEntity(TId id, DateTimeOffset createdAt, Actor createdBy)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(createdBy);

        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntity{TId}"/> class,
    /// reading the creation instant from the supplied <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="id">The strongly-typed identifier for this entity.</param>
    /// <param name="timeProvider">
    /// The time source. Pass <c>TimeProvider.System</c> in production, or a fake provider
    /// in tests. This is an injected dependency, never an ambient static clock.
    /// </param>
    /// <param name="createdBy">The actor creating this entity.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="timeProvider"/> or <paramref name="createdBy"/> is
    /// <see langword="null"/>.
    /// </exception>
    protected AuditableEntity(TId id, TimeProvider timeProvider, Actor createdBy)
        : this(
            id,
            (timeProvider ?? throw new ArgumentNullException(nameof(timeProvider))).GetUtcNow(),
            createdBy)
    {
    }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; protected init; }

    /// <inheritdoc />
    public Actor CreatedBy { get; protected init; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Actor? UpdatedBy { get; private set; }

    /// <summary>
    /// Records that this entity was modified, updating <see cref="UpdatedAt"/> and
    /// <see cref="UpdatedBy"/> together.
    /// </summary>
    /// <param name="updatedAt">The instant of the modification, in UTC.</param>
    /// <param name="updatedBy">The actor performing the modification.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="updatedBy"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Call this at the end of every state-mutating method. Routing both fields through a
    /// single member is what prevents them from drifting apart: with independent setters it
    /// is possible to update the timestamp and forget the actor, producing an audit trail
    /// that records when something changed but not who changed it.
    /// </remarks>
    protected void Touch(DateTimeOffset updatedAt, Actor updatedBy)
    {
        ArgumentNullException.ThrowIfNull(updatedBy);

        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }
}
