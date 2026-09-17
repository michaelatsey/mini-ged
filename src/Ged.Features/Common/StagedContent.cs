using System.Buffers;
using System.Security.Cryptography;
using Ged.Features.Common.FileTypes;

namespace Ged.Features.Common;

/// <summary>Uploaded bytes held aside while their digest and their format are established.</summary>
/// <remarks>
/// <para>
/// Content is addressed by its SHA-256, so the digest has to be known before anything is written to
/// storage — and an upload stream can only be read once. Staging to a temporary file computes the
/// digest during that single read and leaves the bytes replayable.
/// </para>
/// <para>
/// A temporary file rather than memory: uploads are documents, and buffering an arbitrary one in
/// memory turns a large file into an outage for every other request on the process.
/// </para>
/// <para>
/// This is also the quarantine area Microsoft's upload guidance describes. Nothing has reached
/// storage at this point, so a malware scanner slots in here without moving anything afterwards.
/// </para>
/// </remarks>
public sealed class StagedContent : IAsyncDisposable
{
    private const int CopyBufferSize = 81920;

    private readonly string _path;
    private readonly byte[] _header;

    private StagedContent(string path, string digest, long sizeBytes, byte[] header)
    {
        _path = path;
        Digest = digest;
        SizeBytes = sizeBytes;
        _header = header;
    }

    /// <summary>Gets the lowercase hexadecimal SHA-256 of the content.</summary>
    public string Digest { get; }

    /// <summary>Gets the size of the content, in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Gets the first bytes of the content, for format detection.</summary>
    /// <remarks>
    /// Captured during the single pass that already computes the digest, so recognising a format
    /// costs no additional read. Reading the staged file afterwards would mean opening it a second
    /// time for a few hundred bytes.
    /// </remarks>
    public ReadOnlySpan<byte> Header => _header;

    /// <summary>Stages a stream, computing its digest and capturing its header as it is read.</summary>
    /// <param name="content">The incoming content.</param>
    /// <param name="maxSizeBytes">
    /// The ceiling for the format the caller expects. Reading stops as soon as it is exceeded.
    /// </param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The staged content.</returns>
    /// <exception cref="ContentTooLargeException">
    /// Thrown as soon as the ceiling is passed, before the rest of the body is read.
    /// </exception>
    /// <remarks>
    /// The ceiling is enforced <em>during</em> the copy, not after it. Checking the size once the
    /// file is staged means a 250 MB upload claiming to be a CSV is written to disk in full before
    /// being refused for being far over its limit — a cheap way to fill a temp volume, and one the
    /// framework's own body-size limit does not cover, since that limit is set for the largest
    /// format the application accepts.
    /// </remarks>
    public static async Task<StagedContent> CreateAsync(
        Stream content, long maxSizeBytes = long.MaxValue, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSizeBytes);

        var path = Path.Combine(Path.GetTempPath(), $"ged-{Guid.CreateVersion7():N}.staged");
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        var header = new byte[FileTypeInspector.HeaderLength];
        var headerLength = 0;
        var size = 0L;

        try
        {
            byte[] hash;

            await using (var file = new FileStream(
                path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                CopyBufferSize, useAsync: true))
            using (var sha = SHA256.Create())
            await using (var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write))
            {
                int read;

                while ((read = await content.ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct)) > 0)
                {
                    size += read;

                    if (size > maxSizeBytes)
                    {
                        throw new ContentTooLargeException(maxSizeBytes);
                    }

                    if (headerLength < header.Length)
                    {
                        var take = Math.Min(read, header.Length - headerLength);
                        buffer.AsSpan(0, take).CopyTo(header.AsSpan(headerLength));
                        headerLength += take;
                    }

                    await hashing.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                await hashing.FlushFinalBlockAsync(ct);

                hash = sha.Hash!;
            }

            // Counted as the bytes go past rather than read back from the file. FileStream buffers
            // writes, so its length is a question about what has reached the disk — not about how
            // much content arrived, which is what the domain records.
            return new StagedContent(path, Convert.ToHexStringLower(hash), size, header[..headerLength]);
        }
        catch
        {
            TryDelete(path);

            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Opens the staged bytes for reading.</summary>
    /// <returns>A readable stream the caller disposes.</returns>
    public Stream OpenRead() => new FileStream(
        _path, FileMode.Open, FileAccess.Read, FileShare.Read,
        CopyBufferSize, useAsync: true);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        TryDelete(_path);

        return ValueTask.CompletedTask;
    }

    /// <summary>Removes a staged file, never masking the failure that led here.</summary>
    /// <param name="path">The file to remove.</param>
    /// <remarks>
    /// Called from a catch block and from disposal. An exception thrown while cleaning up would
    /// replace the original one, and the caller would be told the temp file could not be deleted
    /// instead of why the upload failed. A leftover file is a job for reconciliation; a lost
    /// exception is a lost diagnosis.
    /// </remarks>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Left for the operating system's temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}

/// <summary>Thrown when an upload passes the ceiling for the format it claims to be.</summary>
/// <param name="maxSizeBytes">The ceiling that was exceeded.</param>
public sealed class ContentTooLargeException(long maxSizeBytes)
    : Exception($"The content exceeds the {maxSizeBytes / (1024 * 1024)} MB limit for this type.")
{
    /// <summary>Gets the ceiling that was exceeded.</summary>
    public long MaxSizeBytes { get; } = maxSizeBytes;
}
