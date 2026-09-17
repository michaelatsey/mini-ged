using Ged.Domain.Folders;

namespace Ged.Domain.Tests.Folders;

public sealed class FolderAncestryTests
{
    [Fact]
    public void An_empty_chain_is_rejected() =>
        Should.Throw<DomainException>(() => FolderAncestry.Of());

    [Fact]
    public void A_repeated_identifier_means_the_stored_hierarchy_already_holds_a_cycle()
    {
        var id = FolderId.New();

        Should.Throw<DomainException>(() => FolderAncestry.Of(id, id));
    }

    [Fact]
    public void Depth_and_self_follow_the_chain()
    {
        var a = FolderId.New();
        var b = FolderId.New();

        var ancestry = FolderAncestry.Of(a, b);

        ancestry.Depth.ShouldBe(2);
        ancestry.Self.ShouldBe(b);
        ancestry.Contains(a).ShouldBeTrue();
        ancestry.Contains(FolderId.New()).ShouldBeFalse();
    }
}
