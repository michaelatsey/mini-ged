namespace Ged.Domain.Folders.Events;

/// <summary>Raised when a folder's business classification changes.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="PreviousType">The classification before the change.</param>
/// <param name="NewType">The classification after the change.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderReclassified(
    Guid FolderId,
    string PreviousType,
    string NewType,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
