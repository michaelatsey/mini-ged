using MicroKit.Domain.Exceptions;

namespace MicroKit.Domain.ValueObjects.Common;

/// <summary>
/// Identifies who performed an action — a human user, a background job, or a system process.
/// </summary>
/// <param name="Value">The raw actor identifier.</param>
/// <remarks>
/// <para>
/// The underlying value is an opaque <see cref="string"/> so that identifiers issued by any
/// identity provider fit without conversion: an OIDC <c>sub</c>, an Entra object id, a
/// Keycloak user id, a service-account name, or a plain GUID rendered as text.
/// </para>
/// <para>
/// Prefer the well-known instances (<see cref="System"/>, <see cref="Anonymous"/>,
/// <see cref="Migration"/>) over a null actor. A named sentinel keeps the audit trail
/// answerable; <see langword="null"/> collapses "unknown", "background job" and "not yet
/// populated" into one indistinguishable value.
/// </para>
/// </remarks>
public sealed record Actor(string Value) : IValueObject
{
    /// <summary>
    /// The maximum supported length of an actor identifier.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Gets the normalized actor identifier.
    /// </summary>
    public string Value { get; } = Validate(Value);

    /// <summary>
    /// The actor used when an action is performed by the application itself,
    /// with no user in the request context (background jobs, schedulers).
    /// </summary>
    public static Actor System { get; } = new("system");

    /// <summary>
    /// The actor used when an action is performed without an authenticated identity.
    /// </summary>
    public static Actor Anonymous { get; } = new("anonymous");

    /// <summary>
    /// The actor used for records created by a data migration rather than by application flow.
    /// </summary>
    public static Actor Migration { get; } = new("migration");

    /// <summary>
    /// Validates and normalizes an actor identifier.
    /// </summary>
    /// <param name="raw">The identifier to validate.</param>
    /// <returns>The trimmed identifier.</returns>
    /// <exception cref="DomainException">
    /// Thrown when the identifier is empty or exceeds <see cref="MaxLength"/>.
    /// </exception>
    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("Actor identifier cannot be null or empty.");

        var trimmed = raw.Trim();

        if (trimmed.Length > MaxLength)
            throw new DomainException($"Actor identifier cannot exceed {MaxLength} characters.");

        return trimmed;
    }

    /// <summary>
    /// Returns the actor identifier.
    /// </summary>
    /// <returns>The actor identifier string.</returns>
    public override string ToString() => Value;
}
