namespace Ged.Domain.Blobs.ValueObjects;

/// <summary>The address of an object within a storage backend: its bucket and key.</summary>
/// <param name="Bucket">The container name.</param>
/// <param name="Key">The object key within the container.</param>
/// <remarks>
/// The domain never composes a key. It stores what an adapter produced and hands it back
/// unchanged, because a naming convention is a property of the backend: the moment the domain
/// builds keys itself, that convention leaks everywhere and can no longer be changed.
/// </remarks>
public sealed record ObjectKey(string Bucket, string Key) : IValueObject
{
    /// <summary>The maximum supported length of a key.</summary>
    public const int MaxKeyLength = 300;

    /// <summary>Gets the container name.</summary>
    public string Bucket { get; } = ValidateBucket(Bucket);

    /// <summary>Gets the object key within the container.</summary>
    public string Key { get; } = ValidateKey(Key);

    private static string ValidateBucket(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? throw new DomainException("A bucket name is required.")
            : raw.Trim();

    private static string ValidateKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("An object key is required.");

        var key = raw.Trim();

        return key.Length <= MaxKeyLength
            ? key
            : throw new DomainException($"An object key cannot exceed {MaxKeyLength} characters.");
    }

    /// <summary>Returns the address in <c>bucket/key</c> form.</summary>
    /// <returns>The address rendered as text.</returns>
    public override string ToString() => $"{Bucket}/{Key}";
}
