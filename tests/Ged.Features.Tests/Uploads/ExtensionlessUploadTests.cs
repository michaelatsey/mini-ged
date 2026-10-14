using System.Text;
using System.Text.Json;
using Ged.Core.Ports.FileTypes;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using Ged.Features.Common.FileTypes;
using Ged.Features.Documents.AddDocumentVersion;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Covers a file name carrying no extension at all — <c>rapport</c>, <c>scan</c> — which scanners,
/// mobile share sheets and some legacy clients produce.
/// </summary>
/// <remarks>
/// <para>
/// Refused unless <see cref="UploadPolicyOptions.AllowMissingExtension"/> says otherwise. When it
/// does, nothing is claimed, so nothing can be contradicted: the content decides, under the same
/// allowlist, document-type narrowing and ceilings as any other upload, and the stored name takes the
/// detected format's extension.
/// </para>
/// <para>
/// The detector is stubbed, as in the reclassification tests. Recognising bytes is the detector's
/// question; what the policy does with the answer is the one under test here.
/// </para>
/// </remarks>
public sealed class ExtensionlessUploadTests
{
    private const long PngCeiling = 25L * 1024 * 1024;

    private const long GlobalCeiling = 256L * 1024 * 1024;

    private static readonly DetectedFormat Pdf = new("pdf", "application/pdf");

    private static readonly DetectedFormat Png = new("png", "image/png");

    private static readonly DetectedFormat Bmp = new("bmp", "image/bmp");

    private static readonly DetectedFormat Doc = new("doc", "application/msword");

    [Fact]
    public void An_extensionless_upload_is_refused_as_NoExtension_when_the_flag_is_off()
    {
        // The behaviour of every deployment that does not state the key, at both gates: the name
        // check, and the content check a caller reaching it directly would otherwise answer with
        // "Files with the '.' extension are not accepted here".
        var policy = Shipped();
        policy.AllowMissingExtension = false;

        CheckName(policy, "rapport").Reason.ShouldBe(RejectionReason.NoExtension);
        Inspect(policy, "rapport", Pdf).Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void An_extensionless_name_passes_the_name_check_bounded_by_the_global_ceiling()
    {
        // No format is claimed, so no per-format ceiling is known before the body is read: the copy
        // is stopped at MaxSizeBytes, and the detected format's own ceiling applies afterwards.
        var decision = CheckName(Shipped(), "rapport");

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format.ShouldBeNull();
        decision.MaxSizeBytes.ShouldBe(GlobalCeiling);
    }

    [Fact]
    public void An_extensionless_pdf_is_accepted_and_stored_with_its_extension_when_the_flag_is_on()
    {
        var decision = Inspect(Shipped(), "rapport", Pdf);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format.ShouldNotBeNull().Name.ShouldBe("pdf");
        decision.MediaType.ShouldBe("application/pdf");
        decision.StoredNameFor("rapport").ShouldBe("rapport.pdf");
    }

    [Fact]
    public void An_extensionless_format_outside_the_allowlist_is_refused()
    {
        // A legacy Office document, macro-capable with nothing in its name to say so. The allowlist
        // is the only gate on what may arrive without an extension, and the shipped one omits it.
        Inspect(Shipped(), "rapport", Doc).Reason.ShouldBe(RejectionReason.ExtensionNotAllowed);
    }

    [Fact]
    public void An_extensionless_format_outside_the_doc_type_narrowing_is_refused()
    {
        var policy = Shipped();
        policy.FormatsByDocType["CONTRACT"] = ["pdf"];

        Inspect(policy, "scan", Png, docType: "CONTRACT").Reason
            .ShouldBe(RejectionReason.ExtensionNotAllowed);
    }

    [Fact]
    public void An_extensionless_upload_over_its_detected_format_ceiling_is_refused_as_TooLarge()
    {
        // 30 MiB passed the name check, bounded by the global 256; the PNG ceiling is 25.
        var decision = Inspect(Shipped(), "scan", Png, sizeBytes: 30L * 1024 * 1024);

        decision.Reason.ShouldBe(RejectionReason.TooLarge);
    }

    [Fact]
    public void An_extensionless_upload_within_its_detected_format_ceiling_carries_that_ceiling()
    {
        var decision = Inspect(Shipped(), "scan", Png, sizeBytes: 20L * 1024 * 1024);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.MaxSizeBytes.ShouldBe(PngCeiling);
    }

    [Fact]
    public void Extensionless_text_is_refused_even_when_the_flag_is_on()
    {
        // txt, csv and json carry no signature, so without a name nothing tells them apart — and
        // picking one would decide which document types may receive it and which media type is
        // recorded. Text that looks like text is still refused.
        var decision = Inspect(
            Shipped(), "notes", detected: null, header: Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n"));

        decision.Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void An_extensionless_bmp_failing_its_header_check_is_refused()
    {
        // Two ASCII letters are no signature on their own; the size the header declares has to
        // agree with the size received, extension or not.
        var decision = Inspect(Shipped(), "scan", Bmp, sizeBytes: 4096, header: BitmapHeader(4095));

        decision.Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void An_extensionless_bmp_passing_its_header_check_is_accepted()
    {
        var decision = Inspect(Shipped(), "scan", Bmp, sizeBytes: 4096, header: BitmapHeader(4096));

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("scan").ShouldBe("scan.bmp");
    }

    [Fact]
    public void An_enforced_declared_media_type_refuses_an_extensionless_name()
    {
        // A deployment that turned EnforceDeclaredMediaType on chose the client's header as a gate,
        // and the name check compares it with the format the extension names. With no extension that
        // comparison has nothing to stand on, so the name is refused rather than let past the gate.
        var policy = Shipped();
        policy.EnforceDeclaredMediaType = true;

        CheckName(policy, "rapport", "application/pdf").Reason.ShouldBe(RejectionReason.NoExtension);
        Inspect(policy, "rapport", Pdf).Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void A_trailing_dot_counts_as_no_extension()
    {
        CheckName(Shipped(), "rapport.").Reason.ShouldBe(RejectionReason.None);

        var decision = Inspect(Shipped(), "rapport.", Pdf);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.StoredNameFor("rapport.").ShouldBe("rapport.pdf");
    }

    [Fact]
    public void A_leading_dot_is_an_extension_and_is_refused_as_one()
    {
        // `.bashrc` names an extension, not an absence of one: the flag does not reach it.
        CheckName(Shipped(), ".bashrc").Reason.ShouldBe(RejectionReason.ExtensionNotAllowed);
        Inspect(Shipped(), ".bashrc", Pdf).Reason.ShouldBe(RejectionReason.ExtensionNotAllowed);
    }

    [Fact]
    public async Task An_extensionless_upload_is_stored_under_the_detected_extension()
    {
        var target = Folder.CreateRoot(new FolderName("Scans"), FolderType.Category, Fixed.Now, Fixed.Me);
        var documents = new FakeDocumentRepository();
        var journal = new Journal();

        var handler = new UploadDocumentHandler(
            new FakeFolderRepository(target),
            new FakeBlobRepository(),
            documents,
            new SingleStorageRegistry(new RecordingStorage(journal)),
            Inspector(),
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Now));

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new UploadDocumentCommand(
            target.Id.Value, "rapport", null, content, Fixed.By));

        outcome.Succeeded.ShouldBeTrue();

        var document = documents.Added.ShouldHaveSingleItem();
        document.Name.Value.ShouldBe("rapport.pdf");
        document.Versions.ShouldHaveSingleItem().MimeType.Value.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task An_extensionless_version_is_stored_under_the_detected_extension()
    {
        var documents = new FakeDocumentRepository();
        var journal = new Journal();

        var document = Document.Create(
            FolderId.New(),
            new DocumentName("contrat.pdf"),
            DocType.Unknown,
            BlobId.FromSha256(new string('c', 64)),
            new MimeType("application/pdf"),
            12,
            Fixed.Now,
            Fixed.Me);

        documents.Seed(document);

        var handler = new AddDocumentVersionHandler(
            documents,
            new FakeBlobRepository(),
            new SingleStorageRegistry(new RecordingStorage(journal)),
            Inspector(),
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Later));

        await using var content = Content.Open();

        var outcome = await handler.HandleAsync(new AddDocumentVersionCommand(
            document.Id.Value, "scan", "from the copier", content, Fixed.By));

        outcome.Succeeded.ShouldBeTrue();

        var version = document.Versions[^1];
        version.FileName.Value.ShouldBe("scan.pdf");
        version.MimeType.Value.ShouldBe("application/pdf");
    }

    [Fact]
    public void The_shipped_configuration_keeps_extensionless_uploads_off()
    {
        // Off in code and stated off in the file that ships, so turning it on is an edit someone
        // reviews rather than a default someone inherits.
        new UploadPolicyOptions().AllowMissingExtension.ShouldBeFalse();

        using var document = JsonDocument.Parse(
            File.ReadAllText(Repository.FileAt(Path.Combine("hosts", "Ged.Api", "appsettings.json"))),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        document.RootElement
            .GetProperty("Ged").GetProperty("Uploads").GetProperty("AllowMissingExtension")
            .GetBoolean()
            .ShouldBeFalse();
    }

    /// <summary>The shipped policy where it matters here, with extensionless uploads switched on.</summary>
    private static UploadPolicyOptions Shipped() => new()
    {
        AllowedFormats = ["pdf", "docx", "txt", "csv", "jpeg", "png", "tiff", "bmp"],
        ReclassifiableGroups = ["images"],
        AllowMissingExtension = true,
        MaxSizeBytes = GlobalCeiling,
        MaxSizeByFormat = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["txt"] = 10L * 1024 * 1024,
            ["csv"] = 10L * 1024 * 1024,
            ["png"] = PngCeiling,
        },
    };

    private static byte[] BitmapHeader(long sizeBytes) =>
    [
        0x42, 0x4D,
        (byte)(sizeBytes & 0xFF),
        (byte)((sizeBytes >> 8) & 0xFF),
        (byte)((sizeBytes >> 16) & 0xFF),
        (byte)((sizeBytes >> 24) & 0xFF),
    ];

    private static FileTypeDecision CheckName(
        UploadPolicyOptions policy, string fileName, string? declaredMediaType = null) =>
        new FileTypeInspector(new StubDetector(null), Options.Create(policy))
            .CheckName(fileName, declaredMediaType, docType: null);

    private static FileTypeDecision Inspect(
        UploadPolicyOptions policy,
        string fileName,
        DetectedFormat? detected,
        long sizeBytes = 4096,
        string? docType = null,
        byte[]? header = null) =>
        new FileTypeInspector(new StubDetector(detected), Options.Create(policy)).Inspect(
            fileName, sizeBytes, header ?? [], () => new MemoryStream([1, 2, 3]), docType);

    /// <summary>The real inspector, detecting a PDF, with extensionless uploads on.</summary>
    private static FileTypeInspector Inspector() => new(
        new StubDetector(Pdf),
        Options.Create(new UploadPolicyOptions { AllowedFormats = ["pdf"], AllowMissingExtension = true }));
}
