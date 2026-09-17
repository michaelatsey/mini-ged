
namespace Ged.Domain.Folders.Events;

/// <summary>Raised when a folder is created.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="ParentId">The parent folder, or null when the folder is a root.</param>
/// <param name="Name">The folder's name.</param>
/// <param name="FolderType">The folder's classification code.</param>
/// <param name="CreatedBy">The actor that created the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderCreated(
    Guid FolderId,
    Guid? ParentId,
    string Name,
    string FolderType,
    string CreatedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
