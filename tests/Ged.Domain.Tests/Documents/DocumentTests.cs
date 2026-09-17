using Ged.Domain.Blobs;
using Ged.Domain.Documents;
using Ged.Domain.Documents.Events;
using Ged.Domain.Documents.Rules;
using Ged.Domain.Folders;

namespace Ged.Domain.Tests.Documents;

public sealed class DocumentTests
{
    private static readonly BlobId BlobA = BlobId.FromSha256(new string('a', 64));
    private static readonly BlobId BlobB = BlobId.FromSha256(new string('b', 64));
    private static readonly FolderId Folder = FolderId.New();

    private static Document NewDocument() => Document.Create(
        Folder, new DocumentName("rapport.pdf"), DocType.Report,
        BlobA, MimeType.Pdf, 1024, Fixed.Now, Fixed.Me);

    [Fact]
    public void Create_always_produces_a_first_version()
    {
        var document = NewDocument();

        document.Versions.Count.ShouldBe(1);
        document.CurrentVersion.Number.Value.ShouldBe(1);
        document.CurrentVersion.BlobId.ShouldBe(BlobA);
    }

    [Fact]
    public void Create_uses_the_supplied_instant_not_the_wall_clock()
    {
        var document = NewDocument();

        document.CreatedAt.ShouldBe(Fixed.Now);
        document.UpdatedAt.ShouldBeNull();
        document.DomainEvents.ShouldHaveSingleItem().OccurredAt.ShouldBe(Fixed.Now);
    }

    [Fact]
    public void AddVersion_with_identical_content_is_refused()
    {
        var document = NewDocument();

        Action act = () => document.AddVersion(
            BlobA, new DocumentName("rapport.pdf"), MimeType.Pdf, 1024, null, Fixed.Later, Fixed.Me);

        act.ShouldBreak<VersionContentMustDifferFromCurrentRule>();
    }

    [Fact]
    public void AddVersion_advances_the_sequence_and_carries_the_previous_content()
    {
        var document = NewDocument();

        var version = document.AddVersion(
            BlobB, new DocumentName("rapport.pdf"), MimeType.Pdf, 2048, "revision", Fixed.Later, Fixed.Me);

        version.Number.Value.ShouldBe(2);
        document.CurrentVersionId.ShouldBe(version.Id);
        document.Versions.Count.ShouldBe(2);
        document.UpdatedAt.ShouldBe(Fixed.Later);

        var raised = document.DomainEvents.OfType<DocumentVersionAdded>().ShouldHaveSingleItem();
        raised.PreviousBlobId.ShouldBe(BlobA.Value);
    }

    [Fact]
    public void RestoreVersion_appends_rather_than_repointing()
    {
        var document = NewDocument();
        var first = document.CurrentVersionId;
        document.AddVersion(BlobB, new DocumentName("r.pdf"), MimeType.Pdf, 2048, null, Fixed.Later, Fixed.Me);

        var restored = document.RestoreVersion(first, Fixed.Latest, Fixed.Me);

        restored.Number.Value.ShouldBe(3);
        document.CurrentVersion.BlobId.ShouldBe(BlobA);
        document.Versions.Count.ShouldBe(3);
    }

    [Fact]
    public void RestoreVersion_of_the_current_version_is_refused()
    {
        var document = NewDocument();

        Action act = () => document.RestoreVersion(document.CurrentVersionId, Fixed.Later, Fixed.Me);

        act.ShouldBreak<VersionMustNotAlreadyBeCurrentRule>();
    }

    [Fact]
    public void RestoreVersion_of_a_foreign_version_is_refused()
    {
        var document = NewDocument();

        Action act = () => document.RestoreVersion(
            Ged.Domain.Documents.DocumentVersionId.New(), Fixed.Later, Fixed.Me);

        act.ShouldBreak<VersionMustBelongToDocumentRule>();
    }

    [Fact]
    public void A_deleted_document_refuses_every_mutation()
    {
        var document = NewDocument();
        document.SoftDelete(Fixed.Later, Fixed.Me);

        var rename = () => document.Rename(new DocumentName("autre.pdf"), Fixed.Latest, Fixed.Me);
        var move = () => document.MoveTo(FolderId.New(), Fixed.Latest, Fixed.Me);

        rename.ShouldBreak<DocumentMustNotBeDeletedRule>();
        move.ShouldBreak<DocumentMustNotBeDeletedRule>();
    }

    [Fact]
    public void SoftDelete_publishes_distinct_blobs_and_destroys_nothing()
    {
        var document = NewDocument();
        document.AddVersion(BlobB, new DocumentName("r.pdf"), MimeType.Pdf, 1, null, Fixed.Later, Fixed.Me);

        document.SoftDelete(Fixed.Latest, Fixed.Me);

        var raised = document.DomainEvents.OfType<DocumentDeleted>().ShouldHaveSingleItem();
        raised.BlobIds.Length.ShouldBe(2);
        document.Versions.Count.ShouldBe(2);
    }

    [Fact]
    public void Renaming_to_the_same_name_raises_nothing()
    {
        var document = NewDocument();
        document.DrainDomainEvents();

        document.Rename(new DocumentName("rapport.pdf"), Fixed.Later, Fixed.Me);

        document.DomainEvents.ShouldBeEmpty();
        document.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void Restore_of_a_live_document_is_refused()
    {
        var document = NewDocument();

        var act = () => document.Restore(Fixed.Later, Fixed.Me);

        act.ShouldBreak<DocumentMustBeDeletedToRestoreRule>();
    }

    [Fact]
    public void DrainDomainEvents_empties_the_collection()
    {
        var document = NewDocument();

        document.DrainDomainEvents().Count.ShouldBe(1);

        document.DomainEvents.ShouldBeEmpty();
    }
}
