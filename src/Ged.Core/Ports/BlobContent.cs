using Ged.Domain.Blobs;

namespace Ged.Core.Ports;

/// <summary>Content opened from a blob, together with how it was obtained.</summary>
/// <param name="Stream">The readable content. The caller disposes it.</param>
/// <param name="ServedBy">The location that answered.</param>
/// <param name="Failures">Locations tried first that could not answer.</param>
/// <remarks>
/// The failures travel back to the caller rather than being logged by the resolver. Reporting a
/// fact and deciding what to do about it are different jobs: the slice knows whether a fallback is
/// routine — it is, during a migration — or whether it means a copy needs repairing.
/// </remarks>
public sealed record BlobContent(
    Stream Stream,
    BlobLocation ServedBy,
    IReadOnlyList<FailedLocation> Failures) : IAsyncDisposable
{
    /// <summary>Gets a value indicating whether a location had to be skipped.</summary>
    public bool ServedFromFallback => Failures.Count > 0;

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await Stream.DisposeAsync();
}

/// <summary>A location that could not serve the content.</summary>
/// <param name="Provider">The backend tried.</param>
/// <param name="Key">The address tried.</param>
/// <param name="Reason">Why it could not answer.</param>
public sealed record FailedLocation(StorageProvider Provider, ObjectKey? Key, string Reason);
