namespace Ged.Domain.Folders.Events;

/// <summary>Raised when a folder is logically deleted.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="ParentId">The parent folder, or null.</param>
/// <param name="DeletedBy">The actor that deleted the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderDeleted(
    Guid FolderId,
    Guid? ParentId,
    string DeletedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
