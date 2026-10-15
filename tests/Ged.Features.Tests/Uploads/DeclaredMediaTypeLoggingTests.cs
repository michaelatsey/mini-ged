using Ged.Core.Ports.FileTypes;
using Ged.Domain.Folders;
using Ged.Features.Common.FileTypes;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Every declared media type that does not name the format it is compared with leaves one entry,
/// whether or not the policy acts on it.
/// </summary>
/// <remarks>
/// <para>
/// The entries are the evidence for <see cref="UploadPolicyOptions.EnforceDeclaredMediaType"/>: which
/// clients mislabel, how often, and whether turning the flag on would refuse anyone. That is why they
/// are written with the flag off too — measuring before deciding is the point.
/// </para>
/// <para>
/// Assertions are on the event id and the structured fields, never on the rendered message.
/// </para>
/// </remarks>
public sealed class DeclaredMediaTypeLoggingTests
{
    private const int BareAccepted = 2001;

    private const int BareRefused = 2002;

    private const int Disagreement = 2003;

    private static readonly DetectedFormat Pdf = new("pdf", "application/pdf");

    private static readonly DetectedFormat Png = new("png", "image/png");

    private static readonly DetectedFormat Jpeg = new("jpg", "image/jpeg");

    private static readonly DetectedFormat Docx =
        new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

    [Fact]
    public void A_mislabel_is_logged_with_the_flag_off_and_changes_nothing()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: false), Png, logger), "photo.png", "image/jpeg");

        decision.Reason.ShouldBe(RejectionReason.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Disagreement);
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Fields["FileName"].ShouldBe("photo.png");
        entry.Fields["DeclaredMediaType"].ShouldBe("image/jpeg");
        entry.Fields["DeclaredFormat"].ShouldBe("jpeg");
        entry.Fields["Format"].ShouldBe("png");
        entry.Fields["Stage"].ShouldBe("content");
        entry.Fields["Enforced"].ShouldBe(false);
        entry.Fields["Outcome"].ShouldBe("accepted");
    }

    [Fact]
    public void The_declared_type_is_logged_as_it_was_compared()
    {
        // Parameters dropped and whitespace trimmed: the entry shows the value the decision used.
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspect(Inspector(Policy(enforce: false), Png, logger), "photo.png", " Image/JPEG ; quality=high");

        logger.Entries.ShouldHaveSingleItem().Fields["DeclaredMediaType"].ShouldBe("Image/JPEG");
    }

    [Fact]
    public void A_mislabel_forgiven_within_its_family_is_logged_as_accepted()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: true), Png, logger), "photo.jpg", "image/jpeg");

        decision.Reason.ShouldBe(RejectionReason.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Disagreement);
        entry.Fields["Enforced"].ShouldBe(true);
        entry.Fields["Outcome"].ShouldBe("accepted");
        entry.Fields["Format"].ShouldBe("png");
    }

    [Fact]
    public void A_claim_across_families_on_a_bare_name_is_logged_beside_its_refusal()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: true), Pdf, logger), "curieux", "image/png");

        decision.Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);

        logger.Entries.Count.ShouldBe(2);
        logger.Entries.ShouldContain(e => e.EventId.Id == BareRefused);

        var entry = logger.Entries.Single(e => e.EventId.Id == Disagreement);
        entry.Fields["Outcome"].ShouldBe("refused");
        entry.Fields["Stage"].ShouldBe("content");
        entry.Fields["DeclaredFormat"].ShouldBe("png");
        entry.Fields["Format"].ShouldBe("pdf");
    }

    [Fact]
    public void An_accepted_bare_upload_with_a_wrong_claim_leaves_its_2001_and_a_2003()
    {
        // Flag off: the bare upload is accepted on its bytes and logged as always; the wrong claim adds
        // its own entry. A count of bare uploads keys on 2001/2002 and still sees one.
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: false), Pdf, logger), "rapport", "image/png");

        decision.Reason.ShouldBe(RejectionReason.None);
        logger.Entries.Count.ShouldBe(2);
        logger.Entries.ShouldContain(e => e.EventId.Id == BareAccepted);

        var entry = logger.Entries.Single(e => e.EventId.Id == Disagreement);
        entry.Fields["FileName"].ShouldBe("rapport");
        entry.Fields["Stage"].ShouldBe("content");
        entry.Fields["Outcome"].ShouldBe("accepted");
        entry.Fields["Enforced"].ShouldBe(false);
        entry.Fields["DeclaredFormat"].ShouldBe("png");
        entry.Fields["Format"].ShouldBe("pdf");
    }

    [Fact]
    public void A_bare_image_claim_corrected_within_its_family_is_logged_as_accepted()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: true), Jpeg, logger), "curieux", "image/png");

        decision.Reason.ShouldBe(RejectionReason.None);
        logger.Entries.ShouldContain(e => e.EventId.Id == BareAccepted);

        var entry = logger.Entries.Single(e => e.EventId.Id == Disagreement);
        entry.Fields["Outcome"].ShouldBe("accepted");
        entry.Fields["Enforced"].ShouldBe(true);
        entry.Fields["DeclaredFormat"].ShouldBe("png");
        entry.Fields["Format"].ShouldBe("jpeg");
    }

    [Fact]
    public void A_bare_name_declared_outside_the_catalogue_is_logged_at_the_name_stage_with_no_format()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspector(Policy(enforce: true), null, logger).CheckName("curieux", "text/html", null);

        decision.Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
        logger.Entries.ShouldContain(e => e.EventId.Id == BareRefused);

        var entry = logger.Entries.Single(e => e.EventId.Id == Disagreement);
        entry.Fields["Stage"].ShouldBe("name");
        entry.Fields["Outcome"].ShouldBe("refused");
        entry.Fields["DeclaredMediaType"].ShouldBe("text/html");
        entry.Fields["DeclaredFormat"].ShouldBeNull();
        entry.Fields["Format"].ShouldBeNull();
    }

    [Fact]
    public void A_claim_refused_before_the_content_is_read_is_logged_at_the_name_stage()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspector(Policy(enforce: true), null, logger).CheckName("rapport.pdf", "image/png", "INVOICE");

        decision.Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Disagreement);
        entry.Fields["Stage"].ShouldBe("name");
        entry.Fields["Outcome"].ShouldBe("refused");
        entry.Fields["Format"].ShouldBe("pdf");
        entry.Fields["DeclaredFormat"].ShouldBe("png");
        entry.Fields["DocType"].ShouldBe("INVOICE");
    }

    [Fact]
    public void A_claim_outside_the_catalogue_is_logged_with_no_declared_format()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: false), Pdf, logger), "rapport.pdf", "text/html");

        decision.Reason.ShouldBe(RejectionReason.None);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Disagreement);
        entry.Fields["DeclaredMediaType"].ShouldBe("text/html");
        entry.Fields["DeclaredFormat"].ShouldBeNull();
        entry.Fields["Outcome"].ShouldBe("accepted");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf")]
    [InlineData("APPLICATION/PDF; charset=binary")]
    public void A_matching_claim_or_no_claim_leaves_no_entry(string? declared)
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspect(Inspector(Policy(enforce: true), Pdf, logger), "rapport.pdf", declared);
        Inspect(Inspector(Policy(enforce: false), Pdf, logger), "rapport.pdf", declared);

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_content_refusal_leaves_no_disagreement()
    {
        // The content never reached a format the claim could disagree with; the refusal already says
        // what the file is.
        var logger = new RecordingLogger<FileTypeInspector>();

        var decision = Inspect(Inspector(Policy(enforce: false), Docx, logger), "rapport.pdf", "image/png");

        decision.Reason.ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_mislabelled_named_upload_through_the_handler_leaves_exactly_one_entry()
    {
        // The handler runs the name check and the content check; the claim agrees at the first (a
        // sibling) and is logged once, at the second.
        var logger = new RecordingLogger<FileTypeInspector>();
        var target = Folder.CreateRoot(new FolderName("Photos"), FolderType.Category, Fixed.Now, Fixed.Me);
        var journal = new Journal();
        var inspector = Inspector(Policy(enforce: true), Png, logger);

        inspector.CheckName("photo.jpg", "image/jpeg", null);

        var handler = new UploadDocumentHandler(
            new FakeFolderRepository(target),
            new FakeBlobRepository(),
            new FakeDocumentRepository(),
            new SingleStorageRegistry(new RecordingStorage(journal)),
            inspector,
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Now));

        await using var content = Content.Open();

        (await handler.HandleAsync(new UploadDocumentCommand(
            target.Id.Value, "photo.jpg", "image/jpeg", null, content, Fixed.By)))
            .Succeeded.ShouldBeTrue();

        logger.Entries.ShouldHaveSingleItem().EventId.Id.ShouldBe(Disagreement);
    }

    private static UploadPolicyOptions Policy(bool enforce) => new()
    {
        AllowedFormats = ["pdf", "docx", "jpeg", "png"],
        ReclassifiableGroups = ["images"],
        AllowMissingExtension = true,
        EnforceDeclaredMediaType = enforce,
    };

    private static FileTypeInspector Inspector(
        UploadPolicyOptions policy, DetectedFormat? detected, RecordingLogger<FileTypeInspector> logger) =>
        new(new StubDetector(detected), Options.Create(policy), logger);

    private static FileTypeDecision Inspect(FileTypeInspector inspector, string fileName, string? declared) =>
        inspector.Inspect(fileName, declared, 4096, [], () => new MemoryStream([1, 2, 3]), docType: null);
}
