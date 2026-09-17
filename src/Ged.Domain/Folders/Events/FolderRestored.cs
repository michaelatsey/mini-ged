namespace Ged.Domain.Folders.Events;

/// <summary>Raised when a logically deleted folder is restored.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="RestoredBy">The actor that restored the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderRestored(
    Guid FolderId,
    string RestoredBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
