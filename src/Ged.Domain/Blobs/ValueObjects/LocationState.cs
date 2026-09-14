namespace Ged.Domain.Blobs.ValueObjects;

/// <summary>The role a location plays for its blob.</summary>
/// <remarks>
/// <para>
/// The four states are the migration path, in order:
/// </para>
/// <code>
/// Migrating  → copy in flight, not readable yet
/// Replica    → copied and verified, readable as a fallback
/// Primary    → the location reads go to; exactly one per blob
/// Legacy     → superseded, kept as a fallback until removal
/// </code>
/// <para>
/// Modelling the copy as a state rather than as a flag is what makes a provider change a data
/// operation: add a location, verify it, switch the primary, drop the old one — with no code
/// change and a two-row rollback.
/// </para>
/// </remarks>
public sealed record LocationState : IValueObject
{
    private LocationState(string code) => Code = code;

    /// <summary>Gets the state code.</summary>
    public string Code { get; }

    /// <summary>A copy is in flight; the location is not readable yet.</summary>
    public static LocationState Migrating { get; } = new("MIGRATING");

    /// <summary>The copy is complete and verified; readable as a fallback.</summary>
    public static LocationState Replica { get; } = new("REPLICA");

    /// <summary>The location reads are served from. Exactly one per blob.</summary>
    public static LocationState Primary { get; } = new("PRIMARY");

    /// <summary>A superseded location, kept as a fallback until it is removed.</summary>
    public static LocationState Legacy { get; } = new("LEGACY");

    private static readonly Dictionary<string, LocationState> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Migrating.Code] = Migrating,
            [Replica.Code] = Replica,
            [Primary.Code] = Primary,
            [Legacy.Code] = Legacy,
        };

    /// <summary>Gets a value indicating whether content can be read from this location.</summary>
    public bool IsReadable => Code != Migrating.Code;

    /// <summary>Rehydrates a state from its code.</summary>
    /// <param name="code">The state code.</param>
    /// <returns>The corresponding state.</returns>
    /// <exception cref="DomainException">Thrown when the code is unknown.</exception>
    public static LocationState From(string code) =>
        !string.IsNullOrWhiteSpace(code) && Known.TryGetValue(code.Trim(), out var state)
            ? state
            : throw new DomainException($"Unknown location state: {code}");

    /// <summary>Returns the state code.</summary>
    /// <returns>The state code.</returns>
    public override string ToString() => Code;
}
