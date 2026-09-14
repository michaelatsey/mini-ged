namespace Ged.Domain.Abstractions;

/// <summary>
/// Identifies who performed an action — a human user, a background job, or a system process.
/// </summary>
/// <param name="Value">The raw actor identifier.</param>
/// <remarks>
/// <para>
/// The underlying value is an opaque <see cref="string"/> so that identifiers issued by any
/// identity provider fit without conversion: an OIDC <c>sub</c>, an Entra object id, a
/// service-account name, or a plain GUID rendered as text.
/// </para>
/// <para>
/// Prefer the well-known instances over an absent actor. A named sentinel keeps the audit
/// trail answerable; <see langword="null"/> collapses "unknown", "background job" and
/// "not yet populated" into one indistinguishable value.
/// </para>
/// </remarks>
public sealed record Actor(string Value) : IValueObject
{
    /// <summary>The maximum supported length of an actor identifier.</summary>
    public const int MaxLength = 200;

    /// <summary>Gets the normalized actor identifier.</summary>
    public string Value { get; } = Validate(Value);

    /// <summary>The actor used when the application acts with no user in the request context.</summary>
    public static Actor System { get; } = new("system");

    /// <summary>The actor used when an action is performed without an authenticated identity.</summary>
    public static Actor Anonymous { get; } = new("anonymous");

    /// <summary>The actor used for records produced by a data migration.</summary>
    public static Actor Migration { get; } = new("migration");

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("An actor identifier cannot be null or empty.");

        var trimmed = raw.Trim();

        return trimmed.Length <= MaxLength
            ? trimmed
            : throw new DomainException($"An actor identifier cannot exceed {MaxLength} characters.");
    }

    /// <summary>Returns the actor identifier.</summary>
    /// <returns>The normalized identifier.</returns>
    public override string ToString() => Value;
}
