namespace Ged.Domain.Blobs.ValueObjects;

/// <summary>Where a blob sits in its lifecycle.</summary>
/// <remarks>
/// Modelled as a value object rather than an <see langword="enum"/> so the persisted value stays
/// readable and so an unrecognized code fails loudly instead of landing on the zero member — which
/// for a lifecycle would silently mean "active".
/// </remarks>
public sealed record BlobStatus : IValueObject
{
    private BlobStatus(string code) => Code = code;

    /// <summary>Gets the status code.</summary>
    public string Code { get; }

    /// <summary>At least one document version still references this content.</summary>
    public static BlobStatus Active { get; } = new("ACTIVE");

    /// <summary>No live reference was found; the retention window is running.</summary>
    public static BlobStatus OrphanCandidate { get; } = new("ORPHAN_CANDIDATE");

    /// <summary>The content has been removed from every backend. Terminal.</summary>
    public static BlobStatus Purged { get; } = new("PURGED");

    private static readonly Dictionary<string, BlobStatus> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Active.Code] = Active,
            [OrphanCandidate.Code] = OrphanCandidate,
            [Purged.Code] = Purged,
        };

    /// <summary>Rehydrates a status from its code.</summary>
    /// <param name="code">The status code.</param>
    /// <returns>The corresponding status.</returns>
    /// <exception cref="DomainException">Thrown when the code is unknown.</exception>
    public static BlobStatus From(string code) =>
        !string.IsNullOrWhiteSpace(code) && Known.TryGetValue(code.Trim(), out var status)
            ? status
            : throw new DomainException($"Unknown blob status: {code}");

    /// <summary>Returns the status code.</summary>
    /// <returns>The status code.</returns>
    public override string ToString() => Code;
}
