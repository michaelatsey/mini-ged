namespace Ged.Domain.Blobs.Identifiers;

/// <summary>
/// Content-addressed identifier of a stored binary: the lowercase hexadecimal SHA-256 of the
/// bytes themselves.
/// </summary>
/// <remarks>
/// Addressing content by its digest rather than by a storage path is what makes deduplication
/// automatic, integrity verifiable, and a provider change a data operation rather than a model
/// change. The identifier belongs to the content, not to the place that happens to hold it.
/// </remarks>
public readonly record struct BlobId : IEntityId
{
    private const int Sha256HexLength = 64;

    private BlobId(string value) => Value = value;

    /// <summary>Gets the lowercase hexadecimal digest.</summary>
    public string Value { get; }

    /// <inheritdoc />
    object IEntityId.Value => Value;

    /// <summary>Creates a blob identifier from a SHA-256 digest.</summary>
    /// <param name="hex">The digest, as 64 hexadecimal characters.</param>
    /// <returns>The corresponding <see cref="BlobId"/>.</returns>
    /// <exception cref="DomainException">Thrown when the value is not a SHA-256 digest.</exception>
    public static BlobId FromSha256(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != Sha256HexLength)
            throw new DomainException("A blob identifier must be a 64-character SHA-256 digest.");

        foreach (var c in hex)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
                throw new DomainException("A blob identifier must contain hexadecimal characters only.");
        }

        return new BlobId(hex.ToLowerInvariant());
    }

    /// <summary>Returns the digest.</summary>
    /// <returns>The lowercase hexadecimal digest.</returns>
    public override string ToString() => Value;
}
