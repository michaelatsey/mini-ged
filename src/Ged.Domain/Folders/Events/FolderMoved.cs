namespace Ged.Domain.Folders.Events;
/// <summary>
/// Raised when a folder changes parent.
/// </summary>
/// <param name="FolderId">The folder.</param>
/// <param name="PreviousParentId">The parent before the move, or null.</param>
/// <param name="NewParentId">The parent after the move, or null when promoted to a root.</param>
/// <param name="MovedBy">The actor that moved the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
/// <remarks>
/// Only the moved folder emits an event; its descendants are untouched aggregates. Consumers
/// maintaining a denormalized path or depth must recompute the whole subtree themselves.
/// </remarks>
public sealed record FolderMoved(
    Guid FolderId,
    Guid? PreviousParentId,
    Guid? NewParentId,
    string MovedBy,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
