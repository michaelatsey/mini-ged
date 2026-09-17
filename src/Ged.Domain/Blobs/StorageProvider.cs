namespace Ged.Domain.Blobs;

/// <summary>The name of a storage backend that can hold content.</summary>
/// <param name="Name">The raw provider name.</param>
/// <remarks>
/// An open set on purpose. The whole point of separating content from its location is that a new
/// provider is a configuration and an adapter, never a change to the domain — so the domain must
/// not hold a closed list that a new backend would force it to reopen. It validates the shape of
/// the name and nothing more.
/// </remarks>
public sealed record StorageProvider(string Name) : IValueObject
{
    /// <summary>The maximum supported length of a provider name.</summary>
    public const int MaxLength = 50;

    /// <summary>Gets the normalized provider name.</summary>
    public string Name { get; } = Validate(Name);

    /// <summary>The legacy system content is migrated away from.</summary>
    public static StorageProvider Beys { get; } = new("beys");

    /// <summary>The S3-compatible object store content is migrated to.</summary>
    public static StorageProvider Minio { get; } = new("minio");

    private static string Validate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException("A storage provider name is required.");

        var name = raw.Trim().ToLowerInvariant();

        if (name.Length > MaxLength)
            throw new DomainException($"A storage provider name cannot exceed {MaxLength} characters.");

        foreach (var c in name)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
            if (!ok)
                throw new DomainException("A storage provider name may contain letters, digits, hyphens and underscores only.");
        }

        return name;
    }

    /// <summary>Returns the provider name.</summary>
    /// <returns>The normalized name.</returns>
    public override string ToString() => Name;
}
