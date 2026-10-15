using Ged.Core.Ports.FileTypes;
using Ged.Features.Common.FileTypes;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Covers <see cref="UploadPolicyOptions.EnforceDeclaredMediaType"/>: a client that states the media
/// type of what it sends — an SDK, an integration — is held to it.
/// </summary>
/// <remarks>
/// <para>
/// The bytes decide, and every claim the client made must agree with them: the declared type is
/// compared with the format finally accepted — named, reclassified, or identified from the content of
/// a bare name — and, like the extension, a wrong label is corrected within a reclassifiable family
/// and refused across one. <c>application/octet-stream</c>, a blank or an absent type claim nothing,
/// and are never a mismatch.
/// </para>
/// <para>
/// The detector is stubbed: what the policy does with its answer is the question here. The same
/// matrix on real bytes, for names with no extension, is <see cref="ExtensionlessDetectionMatrixTests"/>.
/// </para>
/// </remarks>
public sealed class DeclaredMediaTypeTests
{
    private static readonly DetectedFormat Pdf = new("pdf", "application/pdf");

    private static readonly DetectedFormat Png = new("png", "image/png");

    private static readonly DetectedFormat Jpeg = new("jpg", "image/jpeg");

    private static readonly DetectedFormat Docx =
        new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

    // ── the name check ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_named_file_declared_as_its_own_format_passes_the_name_check()
    {
        CheckName(Enforced(), "rapport.pdf", "APPLICATION/PDF; charset=binary")
            .Reason.ShouldBe(RejectionReason.None);
    }

    [Fact]
    public void A_named_file_declared_as_another_format_is_refused_before_its_content_is_read()
    {
        CheckName(Enforced(), "rapport.pdf", "image/png")
            .Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("application/octet-stream")]
    [InlineData("Application/Octet-Stream; name=rapport")]
    [InlineData(";")]
    public void A_type_that_claims_nothing_is_never_a_mismatch_at_the_name_check(string? declared)
    {
        // octet-stream is "arbitrary binary data" (RFC 2046 §4.5.1): it names no format, and it is
        // what an SDK relaying an upload it could not type sends. Refusing it would refuse the caller
        // for not knowing, which is not lying.
        CheckName(Enforced(), "rapport.pdf", declared).Reason.ShouldBe(RejectionReason.None);
        CheckName(Enforced(), "rapport", declared).Reason.ShouldBe(RejectionReason.None);
    }

    [Theory]
    [InlineData("rapport.pdf")]
    [InlineData("rapport")]
    public void A_declared_type_outside_the_catalogue_is_refused_at_the_name_check(string fileName)
    {
        // text/html cannot agree with anything this policy accepts, so there is no reason to read a
        // byte of the body before saying so.
        CheckName(Enforced(), fileName, "text/html")
            .Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
    }

    [Fact]
    public void An_image_declared_as_a_reclassifiable_sibling_passes_the_name_check()
    {
        // The honest client: a PNG saved as photo.jpg, declared as what it is. Reclassification may
        // still make the declared and the accepted formats agree, so the name check lets it through.
        CheckName(Enforced(), "photo.jpg", "image/png").Reason.ShouldBe(RejectionReason.None);
    }

    [Fact]
    public void A_bare_name_with_a_claim_passes_the_name_check_bounded_by_the_global_ceiling()
    {
        var decision = CheckName(Enforced(), "rapport", "application/pdf");

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format.ShouldBeNull();
        decision.MaxSizeBytes.ShouldBe(Enforced().MaxSizeBytes);
    }

    [Fact]
    public void A_bare_name_is_refused_when_extensionless_uploads_are_off_whatever_is_declared()
    {
        var policy = Enforced();
        policy.AllowMissingExtension = false;

        CheckName(policy, "rapport", "application/pdf").Reason.ShouldBe(RejectionReason.NoExtension);
        Inspect(policy, "rapport", "application/pdf", Pdf).Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void With_the_flag_off_the_declared_type_is_ignored_at_the_name_check()
    {
        var policy = Enforced();
        policy.EnforceDeclaredMediaType = false;

        CheckName(policy, "rapport.pdf", "image/png").Reason.ShouldBe(RejectionReason.None);
        CheckName(policy, "rapport", "text/html").Reason.ShouldBe(RejectionReason.None);
    }

    // ── the content check ──────────────────────────────────────────────────────────

    [Fact]
    public void A_bare_name_declared_as_its_content_is_accepted_and_named_after_it()
    {
        var decision = Inspect(Enforced(), "rapport", "application/pdf", Pdf);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("rapport").ShouldBe("rapport.pdf");
        decision.MediaType.ShouldBe("application/pdf");
    }

    [Fact]
    public void A_bare_name_declared_as_something_its_content_is_not_is_refused()
    {
        Inspect(Enforced(), "rapport", "image/png", Pdf)
            .Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/octet-stream")]
    public void A_bare_name_that_claims_nothing_is_decided_by_its_content_alone(string? declared)
    {
        var decision = Inspect(Enforced(), "rapport", declared, Pdf);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("rapport").ShouldBe("rapport.pdf");
    }

    [Fact]
    public void A_bare_text_name_is_refused_whatever_it_declares()
    {
        // The declared type confirms or contradicts; it never identifies. text/csv on a bare name must
        // not choose between the CSV ceiling and the global one.
        Inspect(Enforced(), "notes", "text/plain", detected: null, header: "hello, world\n"u8.ToArray())
            .Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void A_reclassified_image_declared_as_its_name_rather_than_its_content_is_accepted_and_renamed()
    {
        // photo.jpg, declared image/jpeg, holding a PNG. This used to be refused: the claim was held to
        // strict equality while the name was forgiven within its family. A client deriving the type
        // from the name sends exactly this, so the claim is now forgiven the way the name is.
        var decision = Inspect(Enforced(), "photo.jpg", "image/jpeg", Png);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("photo.jpg").ShouldBe("photo.png");
        decision.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public void A_correctly_named_image_declared_as_a_sibling_is_accepted_unchanged()
    {
        var decision = Inspect(Enforced(), "photo.png", "image/jpeg", Png);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("photo.png").ShouldBe("photo.png");
    }

    [Fact]
    public void A_bare_image_declared_as_a_sibling_is_accepted_as_what_it_is()
    {
        var decision = Inspect(Enforced(), "curieux", "image/png", Jpeg);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("curieux").ShouldBe("curieux.jpg");
        decision.MediaType.ShouldBe("image/jpeg");
    }

    [Fact]
    public void A_sibling_claim_need_not_name_an_allowed_format()
    {
        // gif is not in the allowlist, but nothing is stored as a gif: the claim is a wrong label within
        // the family, and the PNG actually stored is allowed. MayStillAgree does not consult the
        // allowlist either.
        Inspect(Enforced(), "photo.jpg", "image/gif", Png).Reason.ShouldBe(RejectionReason.None);
    }

    [Fact]
    public void An_image_claim_on_a_document_is_refused_named_or_bare()
    {
        // Across families the claim changes what the file is said to be, and it stays a mismatch.
        Inspect(Enforced(), "curieux", "image/png", Pdf).Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
        CheckName(Enforced(), "rapport.pdf", "image/png").Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
    }

    [Fact]
    public void Without_a_reclassifiable_family_a_sibling_claim_is_strict_again()
    {
        var policy = Enforced();
        policy.ReclassifiableGroups = [];

        Inspect(policy, "curieux", "image/png", Jpeg).Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);

        // Named, the extension is no longer forgiven either: the content refusal comes first.
        Inspect(policy, "photo.jpg", "image/jpeg", Png).Reason.ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void A_reclassified_image_declared_as_its_content_is_accepted_and_renamed()
    {
        var decision = Inspect(Enforced(), "photo.jpg", "image/png", Png);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("photo.jpg").ShouldBe("photo.png");
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("APPLICATION/PDF; charset=binary")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public void A_named_file_declared_as_its_content_or_as_nothing_is_accepted(string? declared)
    {
        Inspect(Enforced(), "rapport.pdf", declared, Pdf).Reason.ShouldBe(RejectionReason.None);
    }

    [Fact]
    public void A_content_refusal_is_not_replaced_by_a_mismatch()
    {
        // The bytes are a DOCX named .pdf and declared PDF: the content check refuses first, and its
        // reason — the one that names what the file really is — is the one reported.
        Inspect(Enforced(), "rapport.pdf", "application/pdf", Docx)
            .Reason.ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void With_the_flag_off_the_declared_type_is_ignored_at_the_content_check()
    {
        var policy = Enforced();
        policy.EnforceDeclaredMediaType = false;

        Inspect(policy, "rapport.pdf", "image/png", Pdf).Reason.ShouldBe(RejectionReason.None);
        Inspect(policy, "rapport", "image/png", Pdf).Reason.ShouldBe(RejectionReason.None);
    }

    /// <summary>The shipped allowlist, with both flags this class is about switched on.</summary>
    private static UploadPolicyOptions Enforced() => new()
    {
        AllowedFormats = ["pdf", "docx", "xlsx", "pptx", "odt", "ods", "odp",
                          "txt", "csv", "jpeg", "png", "tiff", "bmp"],
        ReclassifiableGroups = ["images"],
        EnforceDeclaredMediaType = true,
        AllowMissingExtension = true,
        MaxSizeBytes = 256L * 1024 * 1024,
        MaxSizeByFormat = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["txt"] = 10L * 1024 * 1024,
            ["csv"] = 10L * 1024 * 1024,
            ["png"] = 25L * 1024 * 1024,
        },
    };

    private static FileTypeDecision CheckName(UploadPolicyOptions policy, string fileName, string? declared) =>
        new FileTypeInspector(new StubDetector(null), Options.Create(policy), NullLogger<FileTypeInspector>.Instance)
            .CheckName(fileName, declared, docType: null);

    private static FileTypeDecision Inspect(
        UploadPolicyOptions policy,
        string fileName,
        string? declared,
        DetectedFormat? detected,
        byte[]? header = null) =>
        new FileTypeInspector(new StubDetector(detected), Options.Create(policy), NullLogger<FileTypeInspector>.Instance)
            .Inspect(fileName, declared, 4096, header ?? [], () => new MemoryStream([1, 2, 3]), docType: null);
}
