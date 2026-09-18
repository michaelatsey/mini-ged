namespace Ged.Domain.Blobs;

/// <summary>How complete a copy is, and whether it has been superseded.</summary>
/// <remarks>
/// <para>
/// The three states are the migration path, in order:
/// </para>
/// <code>
/// Migrating  → copy in flight, not readable yet
/// Replica    → copied and verified, readable
/// Legacy     → superseded, kept as a fallback until removal
/// </code>
/// <para>
/// Which copy reads are served from is deliberately <em>not</em> one of these. That is a
/// cardinality of one, so it lives in the type of a column — <see cref="Blob.PrimaryLocationId"/>
/// — rather than in a state spread over several rows that an index then has to forbid a second
/// of. <see cref="Documents.Document.CurrentVersionId"/> resolves the same question the same way.
/// </para>
/// <para>
/// Modelling the copy as a state rather than as a flag is still what makes a provider change a
/// data operation: add a location, verify it, move the pointer, drop the old one — with no code
/// change and a rollback that is the same call in the other direction.
/// </para>
/// </remarks>
public sealed record LocationState : IValueObject
{
    private LocationState(string code) => Code = code;

    /// <summary>Gets the state code.</summary>
    public string Code { get; }

    /// <summary>A copy is in flight; the location is not readable yet.</summary>
    public static LocationState Migrating { get; } = new("MIGRATING");

    /// <summary>The copy is complete and verified; readable.</summary>
    public static LocationState Replica { get; } = new("REPLICA");

    /// <summary>A superseded location, kept as a fallback until it is removed.</summary>
    public static LocationState Legacy { get; } = new("LEGACY");

    private static readonly Dictionary<string, LocationState> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Migrating.Code] = Migrating,
            [Replica.Code] = Replica,
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
