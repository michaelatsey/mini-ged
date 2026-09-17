using Ged.Core.Ports;
using Ged.Domain.Blobs;
using System.Reflection.Metadata;

namespace Ged.Adapters.Storage;

/// <summary>Reads a blob from the best location available, falling back when one fails.</summary>
/// <param name="registry">The registered storage adapters.</param>
/// <remarks>
/// The fallback is what makes a migration invisible to users: while content is being copied to a new
/// backend, a location that is not there yet costs an entry in
/// <see cref="BlobContent.Failures"/> instead of a failed request.
/// </remarks>
public sealed class BlobLocationResolver(IObjectStorageRegistry registry) : IBlobLocationResolver
{
    /// <inheritdoc />
    public async Task<BlobContent> OpenAsync(Domain.Blobs.Blob blob, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blob);

        if (blob.IsPurged)
            throw new ObjectNotFoundException($"Blob '{blob.Id}' has been purged.");

        var failures = new List<FailedLocation>();

        // ReadOrder is primary, then verified replicas, then legacy copies.
        foreach (var location in blob.ReadOrder)
        {
            if (!registry.TryGet(location.Provider, out var storage) || storage is null)
            {
                failures.Add(new FailedLocation(
                    location.Provider, location.ObjectKey, "no adapter registered for this provider"));

                continue;
            }

            try
            {
                var stream = await storage.OpenAsync(location.ObjectKey, ct);

                return new BlobContent(stream, location, failures);
            }
            catch (ObjectNotFoundException ex)
            {
                failures.Add(new FailedLocation(location.Provider, location.ObjectKey, ex.Message));
            }
        }

        var detail = failures.Count == 0
            ? "the blob has no readable location"
            : string.Join("; ", failures.Select(f => $"{f.Provider}: {f.Reason}"));

        throw new ObjectNotFoundException($"No location could serve blob '{blob.Id}' ({detail}).");
    }
}
