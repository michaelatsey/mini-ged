namespace Ged.Core.Ports;

/// <summary>What a backend should record alongside content.</summary>
/// <param name="SizeBytes">The size of the content, in bytes, or null when not yet known.</param>
/// <param name="MediaType">The media type, or null when unidentified.</param>
public readonly record struct ObjectMetadata(long? SizeBytes, string? MediaType)
{
    /// <summary>Metadata carrying nothing.</summary>
    public static ObjectMetadata None => new(null, null);
}
