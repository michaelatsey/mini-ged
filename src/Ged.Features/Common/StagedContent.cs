using System.Security.Cryptography;

namespace Ged.Features.Common;

/// <summary>Uploaded bytes held aside while their digest is computed.</summary>
/// <remarks>
/// <para>
/// Content is addressed by its SHA-256, so the digest has to be known before anything is written to
/// storage — and an upload stream can only be read once. Staging to a temporary file computes the
/// digest during the single read and leaves the bytes replayable.
/// </para>
/// <para>
/// A temporary file rather than memory: uploads are documents, and buffering an arbitrary one in
/// memory turns a large file into an outage for every other request on the process.
/// </para>
/// </remarks>
public sealed class StagedContent : IAsyncDisposable
{
    private readonly string _path;

    private StagedContent(string path, string digest, long sizeBytes)
    {
        _path = path;
        Digest = digest;
        SizeBytes = sizeBytes;
    }

    /// <summary>Gets the lowercase hexadecimal SHA-256 of the content.</summary>
    public string Digest { get; }

    /// <summary>Gets the size of the content, in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Stages a stream, computing its digest as it is read.</summary>
    /// <param name="content">The incoming content.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The staged content.</returns>
    public static async Task<StagedContent> CreateAsync(Stream content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = Path.Combine(Path.GetTempPath(), $"ged-{Guid.CreateVersion7():N}.staged");

        try
        {
            long size;
            byte[] hash;

            await using (var file = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            using (var sha = SHA256.Create())
            await using (var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write))
            {
                await content.CopyToAsync(hashing, ct);
                await hashing.FlushFinalBlockAsync(ct);

                size = file.Length;
                hash = sha.Hash!;
            }

            return new StagedContent(path, Convert.ToHexStringLower(hash), size);
        }
        catch
        {
            if (File.Exists(path))
                File.Delete(path);

            throw;
        }
    }

    /// <summary>Opens the staged bytes for reading.</summary>
    /// <returns>A readable stream the caller disposes.</returns>
    public Stream OpenRead() => new FileStream(
        _path, FileMode.Open, FileAccess.Read, FileShare.Read,
        bufferSize: 81920, useAsync: true);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (File.Exists(_path))
            File.Delete(_path);

        return ValueTask.CompletedTask;
    }
}
