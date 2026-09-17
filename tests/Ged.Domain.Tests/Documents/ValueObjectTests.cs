using Ged.Domain.Blobs;
using Ged.Domain.Documents;

namespace Ged.Domain.Tests.Documents;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b.pdf")]
    [InlineData("a\\b.pdf")]
    [InlineData("..")]
    [InlineData(".")]
    public void DocumentName_rejects_invalid_values(string raw) =>
        Should.Throw<DomainException>(() => new DocumentName(raw));

    [Fact]
    public void DocumentName_trims_and_exposes_its_extension()
    {
        var name = new DocumentName("  rapport.PDF  ");

        name.Value.ShouldBe("rapport.PDF");
        name.Extension.ShouldBe("pdf");
    }

    [Fact]
    public void DocType_falls_back_to_unknown_but_rejects_unrecognized_codes()
    {
        new DocType("").ShouldBe(DocType.Unknown);
        new DocType("contract").ShouldBe(DocType.Contract);
        Should.Throw<DomainException>(() => new DocType("NOPE"));
    }

    [Fact]
    public void MimeType_validates_shape_only()
    {
        new MimeType("APPLICATION/PDF").ShouldBe(MimeType.Pdf);
        new MimeType("").ShouldBe(MimeType.Octet);
        Should.Throw<DomainException>(() => new MimeType("pdf"));
    }

    [Fact]
    public void VersionNumber_can_only_advance_one_step_at_a_time()
    {
        VersionNumber.First.Value.ShouldBe(1);
        VersionNumber.First.Next().Value.ShouldBe(2);
        Should.Throw<DomainException>(() => VersionNumber.From(0));
    }

    [Fact]
    public void BlobId_requires_a_sha256_digest()
    {
        BlobId.FromSha256(new string('A', 64)).Value.ShouldBe(new string('a', 64));
        Should.Throw<DomainException>(() => BlobId.FromSha256("abc"));
        Should.Throw<DomainException>(() => BlobId.FromSha256(new string('z', 64)));
    }

    [Fact]
    public void Actor_rejects_an_empty_identity()
    {
        Should.Throw<DomainException>(() => new Actor("  "));
        Actor.System.Value.ShouldBe("system");
    }
}
