using Ged.Domain.Folders;
using Ged.Domain.Folders.Events;
using Ged.Domain.Folders.Rules;

namespace Ged.Domain.Tests.Folders;

public sealed class FolderTests
{
    private static Folder NewRoot() =>
        Folder.CreateRoot(new FolderName("Dossiers"), FolderType.Category, Fixed.Now, Fixed.Me);

    private static Folder ChildOf(Folder parent, params FolderId[] above) =>
        Folder.CreateChild(
            FolderAncestry.Of([.. above, parent.Id]),
            new FolderName("Enfant"), FolderType.Case, Fixed.Now, Fixed.Me);

    [Fact]
    public void CreateRoot_has_no_parent()
    {
        var folder = NewRoot();

        folder.IsRoot.ShouldBeTrue();
        folder.ParentId.ShouldBeNull();
        folder.CreatedAt.ShouldBe(Fixed.Now);
    }

    [Fact]
    public void CreateChild_attaches_to_its_parent()
    {
        var root = NewRoot();

        var child = ChildOf(root);

        child.ParentId.ShouldBe(root.Id);
        child.DomainEvents.OfType<FolderCreated>().ShouldHaveSingleItem()
            .ParentId.ShouldBe(root.Id.Value);
    }

    [Fact]
    public void CreateChild_enforces_the_depth_limit()
    {
        var fullChain = Enumerable.Range(0, FolderAncestry.MaxDepth)
            .Select(_ => FolderId.New())
            .ToArray();

        Action act = () => Folder.CreateChild(
            FolderAncestry.Of(fullChain), new FolderName("Trop profond"),
            FolderType.Case, Fixed.Now, Fixed.Me);

        act.ShouldBreak<FolderDepthMustNotExceedLimitRule>();
    }

    [Fact]
    public void A_folder_cannot_become_its_own_parent()
    {
        var folder = NewRoot();

        var act = () => folder.MoveTo(FolderAncestry.Of(folder.Id), Fixed.Later, Fixed.Me);

        act.ShouldBreak<FolderMustNotBeItsOwnParentRule>();
    }

    [Fact]
    public void A_folder_cannot_be_moved_into_its_own_subtree()
    {
        var a = NewRoot();
        var b = ChildOf(a);
        var c = ChildOf(b, a.Id);

        var act = () => a.MoveTo(FolderAncestry.Of(a.Id, b.Id, c.Id), Fixed.Later, Fixed.Me);

        act.ShouldBreak<FolderMustNotMoveIntoItsOwnSubtreeRule>();
    }

    [Fact]
    public void MoveToRoot_clears_the_parent()
    {
        var root = NewRoot();
        var child = ChildOf(root);

        child.MoveToRoot(Fixed.Later, Fixed.Me);

        child.IsRoot.ShouldBeTrue();
        child.DomainEvents.OfType<FolderMoved>().ShouldHaveSingleItem()
            .NewParentId.ShouldBeNull();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_folder_that_still_holds_content_cannot_be_deleted(bool folders, bool documents)
    {
        var folder = NewRoot();

        var act = () => folder.SoftDelete(folders, documents, Fixed.Later, Fixed.Me);

        act.ShouldBreak<FolderMustBeEmptyToDeleteRule>();
    }

    [Fact]
    public void An_empty_folder_can_be_deleted_and_restored()
    {
        var folder = NewRoot();

        folder.SoftDelete(false, false, Fixed.Later, Fixed.Me);
        folder.IsDeleted.ShouldBeTrue();

        folder.Restore(Fixed.Latest, Fixed.Me);
        folder.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void A_deleted_folder_refuses_every_mutation()
    {
        var folder = NewRoot();
        folder.SoftDelete(false, false, Fixed.Later, Fixed.Me);

        var act = () => folder.Rename(new FolderName("Autre"), Fixed.Latest, Fixed.Me);

        act.ShouldBreak<FolderMustNotBeDeletedRule>();
    }

    [Fact]
    public void Restoring_a_live_folder_is_refused()
    {
        var act = () => NewRoot().Restore(Fixed.Later, Fixed.Me);

        act.ShouldBreak<FolderMustBeDeletedToRestoreRule>();
    }
}
