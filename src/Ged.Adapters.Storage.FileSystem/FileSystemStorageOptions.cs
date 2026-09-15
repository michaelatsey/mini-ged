namespace Ged.Adapters.Storage.FileSystem;

/// <summary>Where the filesystem backend keeps its objects.</summary>
public sealed class FileSystemStorageOptions
{
    /// <summary>Gets or sets the directory holding the buckets.</summary>
    public string RootPath { get; set; } = "./ged-storage";

    /// <summary>Gets or sets the container new objects are written to.</summary>
    public string DefaultBucket { get; set; } = "ged";

    /// <summary>Gets or sets the provider name recorded on locations.</summary>
    /// <remarks>
    /// Stored in the database on every location, so changing it after content exists orphans those
    /// rows: the registry would no longer find an adapter for the old name.
    /// </remarks>
    public string ProviderName { get; set; } = "filesystem";
}
