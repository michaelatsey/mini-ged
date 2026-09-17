namespace Ged.Domain.Folders.Events;

/// <summary>Raised when a folder is renamed.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="PreviousName">The name before the change.</param>
/// <param name="NewName">The name after the change.</param>
/// <param name="RenamedBy">The actor that renamed the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderRenamed(
    Guid FolderId,
    string PreviousName,
    string NewName,
    string RenamedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
