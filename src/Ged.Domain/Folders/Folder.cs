using Ged.Domain.Folders.Events;
using Ged.Domain.Folders.Rules;

namespace Ged.Domain.Folders;

/// <summary>
/// A container in the document hierarchy: the business context a document belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate holds a single node. It knows its parent and nothing else — not its child
/// folders, not its documents. Both are separate aggregates with their own consistency boundary,
/// and pulling them in would make an unrelated document upload contend with a folder rename.
/// </para>
/// <para>
/// Hierarchy rules that need more than one node — cycle prevention, depth limits, emptiness on
/// delete — take their facts as parameters. The aggregate performs no I/O and owns the decision;
/// the caller supplies the observation.
/// </para>
/// </remarks>
public sealed class Folder : AuditableAggregateRoot<FolderId>
{
    private Folder(
        FolderId id,
        FolderId? parentId,
        FolderName name,
        FolderType type,
        DateTimeOffset createdAt,
        Actor createdBy)
        : base(id, createdAt, createdBy)
    {
        ParentId = parentId;
        Name = name;
        Type = type;
    }

    /// <summary>Gets the parent folder, or null when this folder is a root.</summary>
    public FolderId? ParentId { get; private set; }

    /// <summary>Gets the folder's display name.</summary>
    public FolderName Name { get; private set; }

    /// <summary>Gets the folder's business classification.</summary>
    public FolderType Type { get; private set; }

    /// <summary>Gets when this folder was logically deleted, if it was.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Gets a value indicating whether this folder sits at the top of a hierarchy.</summary>
    public bool IsRoot => ParentId is null;

    /// <summary>Gets a value indicating whether this folder is logically deleted.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Creates a folder at the top of a hierarchy, with no parent.</summary>
    /// <param name="name">The folder's display name.</param>
    /// <param name="type">The folder's business classification.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The newly created root folder.</returns>
    public static Folder CreateRoot(FolderName name, FolderType type, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(by);

        var folder = new Folder(FolderId.New(), parentId: null, name, type, now, by);

        folder.RaiseDomainEvent(new FolderCreated(
            folder.Id.Value, null, name.Value, type.Code, by.Value, now));

        return folder;
    }

    /// <summary>Creates a folder beneath an existing one.</summary>
    /// <param name="parentAncestry">
    /// The ancestry of the parent folder, from the root down to the parent itself. Supplies both
    /// the parent's identity and the depth the new folder would occupy.
    /// </param>
    /// <param name="name">The folder's display name.</param>
    /// <param name="type">The folder's business classification.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <returns>The newly created child folder.</returns>
    public static Folder CreateChild(
        FolderAncestry parentAncestry,
        FolderName name,
        FolderType type,
        DateTimeOffset now,
        Actor by)
    {
        ArgumentNullException.ThrowIfNull(parentAncestry);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new FolderDepthMustNotExceedLimitRule(parentAncestry.Depth + 1));

        var folder = new Folder(FolderId.New(), parentAncestry.Self, name, type, now, by);

        folder.RaiseDomainEvent(new FolderCreated(
            folder.Id.Value, parentAncestry.Self.Value, name.Value, type.Code, by.Value, now));

        return folder;
    }

    /// <summary>Renames the folder.</summary>
    /// <param name="newName">The new display name.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void Rename(FolderName newName, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(newName);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new FolderMustNotBeDeletedRule(DeletedAt));

        if (Name == newName) return;

        var previous = Name;
        Name = newName;
        Touch(now, by);

        RaiseDomainEvent(new FolderRenamed(Id.Value, previous.Value, newName.Value, by.Value, now));
    }

    /// <summary>Changes the folder's business classification.</summary>
    /// <param name="newType">The new classification.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void Reclassify(FolderType newType, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(newType);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new FolderMustNotBeDeletedRule(DeletedAt));

        if (Type == newType) return;

        var previous = Type;
        Type = newType;
        Touch(now, by);

        RaiseDomainEvent(new FolderReclassified(Id.Value, previous.Code, newType.Code, now));
    }

    /// <summary>Moves the folder beneath another folder.</summary>
    /// <param name="targetAncestry">
    /// The ancestry of the proposed new parent, from the root down to the parent itself.
    /// </param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// Depth is validated for this folder only. The aggregate cannot see its own subtree, so a
    /// move that pushes a deep descendant past the limit is not caught here — see
    /// <c>docs/domain-boundaries.md</c> for the caller's share of that responsibility.
    /// </remarks>
    public void MoveTo(FolderAncestry targetAncestry, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(targetAncestry);
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new FolderMustNotBeDeletedRule(DeletedAt));
        CheckRule(new FolderMustNotBeItsOwnParentRule(Id, targetAncestry.Self));
        CheckRule(new FolderMustNotMoveIntoItsOwnSubtreeRule(Id, targetAncestry));
        CheckRule(new FolderDepthMustNotExceedLimitRule(targetAncestry.Depth + 1));

        if (ParentId == targetAncestry.Self) return;

        var previous = ParentId;
        ParentId = targetAncestry.Self;
        Touch(now, by);

        RaiseDomainEvent(new FolderMoved(
            Id.Value, previous?.Value, targetAncestry.Self.Value, by.Value, now));
    }

    /// <summary>Promotes the folder to the top of the hierarchy.</summary>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void MoveToRoot(DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new FolderMustNotBeDeletedRule(DeletedAt));

        if (IsRoot) return;

        var previous = ParentId;
        ParentId = null;
        Touch(now, by);

        RaiseDomainEvent(new FolderMoved(Id.Value, previous?.Value, null, by.Value, now));
    }

    /// <summary>Logically deletes the folder.</summary>
    /// <param name="hasChildFolders">Whether at least one non-deleted child folder remains.</param>
    /// <param name="hasDocuments">Whether at least one non-deleted document remains.</param>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    /// <remarks>
    /// Nothing cascades: child folders and documents are separate aggregates and are not touched.
    /// Deleting a folder marks this node and this node only.
    /// </remarks>
    public void SoftDelete(bool hasChildFolders, bool hasDocuments, DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        if (IsDeleted) return;

        CheckRule(new FolderMustBeEmptyToDeleteRule(hasChildFolders, hasDocuments));

        DeletedAt = now;
        Touch(now, by);

        RaiseDomainEvent(new FolderDeleted(Id.Value, ParentId?.Value, by.Value, now));
    }

    /// <summary>Restores a logically deleted folder.</summary>
    /// <param name="now">The instant of the operation, in UTC.</param>
    /// <param name="by">The actor performing the operation.</param>
    public void Restore(DateTimeOffset now, Actor by)
    {
        ArgumentNullException.ThrowIfNull(by);

        CheckRule(new FolderMustBeDeletedToRestoreRule(DeletedAt));

        DeletedAt = null;
        Touch(now, by);

        RaiseDomainEvent(new FolderRestored(Id.Value, by.Value, now));
    }
}
