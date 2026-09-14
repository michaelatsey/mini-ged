using Ged.Domain.Abstractions;

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

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
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

/// <summary>Raised when a folder's business classification changes.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="PreviousType">The classification before the change.</param>
/// <param name="NewType">The classification after the change.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderReclassified(
    Guid FolderId,
    string PreviousType,
    string NewType,
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

/// <summary>Raised when a folder is logically deleted.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="ParentId">The parent folder, or null.</param>
/// <param name="DeletedBy">The actor that deleted the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderDeleted(
    Guid FolderId,
    Guid? ParentId,
    string DeletedBy,
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);

/// <summary>Raised when a logically deleted folder is restored.</summary>
/// <param name="FolderId">The folder.</param>
/// <param name="RestoredBy">The actor that restored the folder.</param>
/// <param name="OccurredAt">The instant of the operation, in UTC.</param>
public sealed record FolderRestored(
    Guid FolderId,
    string RestoredBy,
    DateTimeOffset OccurredAt) : GedDomainEvent(OccurredAt);
